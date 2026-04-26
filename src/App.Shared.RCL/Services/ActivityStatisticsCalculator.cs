using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>Shared aggregation logic for activity statistics (web DB and MAUI local store).</summary>
public static class ActivityStatisticsCalculator
{
    private const int HeatmapDataDays = 370;

    public static IReadOnlyList<DailyGraphPeriodOption> BuildPeriodOptions(
        DateOnly utcToday,
        IReadOnlyList<UserActivityEventRecord> allUserEvents)
    {
        int maxYear = utcToday.Year;
        UserActivityEventRecord? first = allUserEvents
            .Where(e => e.EventType == ActivityEventType.DailyComplete)
            .OrderBy(e => e.OccurredAtUtc)
            .FirstOrDefault();

        int minYear = first is { } f ? f.OccurredAtUtc.UtcDateTime.Year : maxYear;
        if (minYear > maxYear)
        {
            minYear = maxYear;
        }

        var list = new List<DailyGraphPeriodOption>
        {
            new(DailyGraphPeriods.Rolling370Days, $"Last {HeatmapDataDays} days"),
        };
        for (int y = maxYear; y >= minYear; y--)
        {
            list.Add(new DailyGraphPeriodOption(DailyGraphPeriods.ForCalendarYear(y), y.ToString()));
        }

        return list;
    }

    public static (string Key, DateOnly Start, DateOnly End) ResolveActivityPeriod(
        string? periodKey,
        DateOnly utcToday,
        IReadOnlyList<DailyGraphPeriodOption> options)
    {
        HashSet<string> optionKeys = [.. options.Select(o => o.Key)];
        string key;
        if (string.IsNullOrWhiteSpace(periodKey))
        {
            key = DailyGraphPeriods.Rolling370Days;
        }
        else if (!optionKeys.Contains(periodKey))
        {
            key = DailyGraphPeriods.Rolling370Days;
        }
        else
        {
            key = periodKey;
        }

        if (string.Equals(key, DailyGraphPeriods.Rolling370Days, StringComparison.Ordinal))
        {
            DateOnly rangeEnd = utcToday;
            DateOnly rangeStart = rangeEnd.AddDays(-(HeatmapDataDays - 1));
            return (key, rangeStart, rangeEnd);
        }

        if (key.StartsWith(DailyGraphPeriods.YearPrefix, StringComparison.Ordinal) &&
            int.TryParse(key.AsSpan(DailyGraphPeriods.YearPrefix.Length), out int y))
        {
            return (key, new DateOnly(y, 1, 1), new DateOnly(y, 12, 31));
        }

        return (DailyGraphPeriods.Rolling370Days, utcToday.AddDays(-(HeatmapDataDays - 1)), utcToday);
    }

    public static ActivityDashboardDto BuildDashboard(
        IReadOnlyList<UserActivityEventRecord> rows,
        string periodKey,
        DateOnly start,
        DateOnly end,
        DateOnly utcToday)
    {
        var perDay = new Dictionary<DateOnly, (int count, int focusSec)>();
        foreach (UserActivityEventRecord r in rows)
        {
            var d = DateOnly.FromDateTime(r.OccurredAtUtc.UtcDateTime);
            if (!perDay.TryGetValue(d, out var acc))
            {
                acc = (0, 0);
            }

            int focus = r is { EventType: ActivityEventType.TimerSession, DurationSeconds: int s } ? s : 0;
            perDay[d] = (acc.count + 1, acc.focusSec + focus);
        }

        int maxDayCount = 0;
        foreach (var kv in perDay)
        {
            if (kv.Value.count > maxDayCount)
            {
                maxDayCount = kv.Value.count;
            }
        }

        int totalEvents = rows.Count;
        int totalFocusSec = rows
            .Where(x => x.EventType == ActivityEventType.TimerSession)
            .Sum(x => x.DurationSeconds.GetValueOrDefault());
        int totalFocusMinutes = (totalFocusSec + 30) / 60;

        DateOnly startWeek = StartOfIsoWeek(start);
        DateOnly endWeek = StartOfIsoWeek(end);
        int weekSpan = (endWeek.DayNumber - startWeek.DayNumber) / 7;
        if (weekSpan < 0)
        {
            weekSpan = 0;
        }

        int weekCount = weekSpan + 1;
        var weekBars = new List<ActivityWeekBarDto>(weekCount);
        for (int i = 0; i < weekCount; i++)
        {
            DateOnly ws = startWeek.AddDays(i * 7);
            DateOnly we = ws.AddDays(6);
            DateOnly clipFrom = ws < start ? start : ws;
            DateOnly clipTo = we > end ? end : we;

            if (clipFrom > clipTo)
            {
                weekBars.Add(new ActivityWeekBarDto(i, ws, 0, 0));
                continue;
            }

            int ev = 0;
            int focus = 0;
            for (var d = clipFrom; d <= clipTo; d = d.AddDays(1))
            {
                if (perDay.TryGetValue(d, out var t))
                {
                    ev += t.count;
                    focus += t.focusSec;
                }
            }

            weekBars.Add(new ActivityWeekBarDto(i, ws, ev, (focus + 30) / 60));
        }

        DateOnly weekBarsRangeStart = weekCount > 0 ? weekBars[0].WeekStart : startWeek;
        DateOnly weekBarsRangeEnd = weekCount > 0 ? weekBars[^1].WeekStart.AddDays(6) : end;

        int heatmapSpanDays = end.DayNumber - start.DayNumber + 1;
        if (heatmapSpanDays < 1)
        {
            heatmapSpanDays = 1;
        }

        DateOnly gridStartMonday = StartOfIsoWeek(start);
        DateOnly endMonday = StartOfIsoWeek(end);
        int gridWeekColumns = (endMonday.DayNumber - gridStartMonday.DayNumber) / 7 + 1;
        if (gridWeekColumns < 1)
        {
            gridWeekColumns = 1;
        }

        var heat = new List<ActivityHeatmapCellDto>(gridWeekColumns * 7);
        for (int c = 0; c < gridWeekColumns; c++)
        {
            for (int r = 0; r < 7; r++)
            {
                DateOnly date = gridStartMonday.AddDays(c * 7 + r);
                bool inRange = date >= start && date <= end && date <= utcToday;
                if (!inRange)
                {
                    heat.Add(new ActivityHeatmapCellDto(r, c, date, 0, 0, false));
                }
                else
                {
                    int count = perDay.TryGetValue(date, out var t) ? t.count : 0;
                    int intensity = IntensityFor(count, maxDayCount);
                    heat.Add(new ActivityHeatmapCellDto(r, c, date, count, intensity, true));
                }
            }
        }

        return new ActivityDashboardDto(
            periodKey,
            weekBars,
            heat,
            gridWeekColumns,
            totalEvents,
            totalFocusMinutes,
            maxDayCount,
            start,
            end,
            heatmapSpanDays,
            weekBarsRangeStart,
            weekBarsRangeEnd);
    }

    public static DailyContributionsViewDto BuildDailyContributions(
        IReadOnlyList<UserActivityEventRecord> eventRowsInRange,
        IReadOnlyList<(Guid Id, string Title)> dailyItemRows,
        string periodKey,
        IReadOnlyList<DailyGraphPeriodOption> options,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        DateOnly utcToday)
    {
        if (dailyItemRows.Count == 0)
        {
            return new DailyContributionsViewDto(
                periodKey,
                options,
                [],
                rangeStart,
                rangeEnd);
        }

        var dailyIds = dailyItemRows.Select(x => x.Id).ToHashSet();
        List<UserActivityEventRecord> filtered = eventRowsInRange
            .Where(e =>
                e.EventType == ActivityEventType.DailyComplete &&
                e.BoardItemId != null &&
                dailyIds.Contains(e.BoardItemId.Value))
            .ToList();

        var byItem = new Dictionary<Guid, Dictionary<DateOnly, int>>();
        foreach (UserActivityEventRecord r in filtered)
        {
            Guid id = r.BoardItemId!.Value;
            var d = DateOnly.FromDateTime(r.OccurredAtUtc.UtcDateTime);
            if (!byItem.TryGetValue(id, out var map))
            {
                map = [];
                byItem[id] = map;
            }

            map[d] = map.GetValueOrDefault(d) + 1;
        }

        var graphs = new List<DailyContributionGraphDto>(dailyItemRows.Count);
        foreach (var di in dailyItemRows)
        {
            byItem.TryGetValue(di.Id, out var countByDay);
            countByDay ??= [];

            int maxInRange = 0;
            for (var d = rangeStart; d <= rangeEnd; d = d.AddDays(1))
            {
                if (d > utcToday)
                {
                    break;
                }

                if (countByDay.TryGetValue(d, out int c) && c > maxInRange)
                {
                    maxInRange = c;
                }
            }

            IReadOnlyList<ActivityHeatmapCellDto> graphHeat = BuildRangeContributionHeatmap(
                rangeStart,
                rangeEnd,
                utcToday,
                countByDay,
                maxInRange);

            DateOnly gStartW = StartOfIsoWeek(rangeStart);
            DateOnly gEndW = StartOfIsoWeek(rangeEnd);
            int columns = (gEndW.DayNumber - gStartW.DayNumber) / 7 + 1;
            if (columns < 1)
            {
                columns = 1;
            }

            graphs.Add(new DailyContributionGraphDto(di.Id, di.Title, graphHeat, columns, maxInRange));
        }

        return new DailyContributionsViewDto(
            periodKey,
            options,
            graphs,
            rangeStart,
            rangeEnd);
    }

    public static ActivityDayDetailDto BuildDayDetail(
        DateOnly day,
        IReadOnlyList<UserActivityEventRecord> rows,
        IReadOnlyDictionary<Guid, string> titles)
    {
        var list = new List<ActivityDayEventDto>(rows.Count);
        foreach (UserActivityEventRecord r in rows.OrderBy(x => x.OccurredAtUtc))
        {
            string? itemTitle = r.BoardItemId is { } id ? titles.GetValueOrDefault(id) : null;
            list.Add(new ActivityDayEventDto(
                r.OccurredAtUtc,
                r.EventType,
                MapEventTypeLabel(r.EventType),
                itemTitle,
                r.DurationSeconds));
        }

        return new ActivityDayDetailDto(day, list);
    }

    private static List<ActivityHeatmapCellDto> BuildRangeContributionHeatmap(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        DateOnly utcToday,
        IReadOnlyDictionary<DateOnly, int> countByDay,
        int maxInRange)
    {
        DateOnly gridStartMonday = StartOfIsoWeek(rangeStart);
        DateOnly endMonday = StartOfIsoWeek(rangeEnd);
        int gridWeekColumns = (endMonday.DayNumber - gridStartMonday.DayNumber) / 7 + 1;
        if (gridWeekColumns < 1)
        {
            gridWeekColumns = 1;
        }

        var heat = new List<ActivityHeatmapCellDto>(gridWeekColumns * 7);
        for (int c = 0; c < gridWeekColumns; c++)
        {
            for (int r = 0; r < 7; r++)
            {
                DateOnly date = gridStartMonday.AddDays(c * 7 + r);
                bool inWindow = date >= rangeStart && date <= rangeEnd;
                if (!inWindow)
                {
                    heat.Add(new ActivityHeatmapCellDto(r, c, date, 0, 0, false));
                    continue;
                }

                if (date > utcToday)
                {
                    heat.Add(new ActivityHeatmapCellDto(r, c, date, 0, 0, false));
                    continue;
                }

                int count = countByDay.TryGetValue(date, out int n) ? n : 0;
                int intensity = IntensityFor(count, maxInRange);
                heat.Add(new ActivityHeatmapCellDto(r, c, date, count, intensity, true));
            }
        }

        return heat;
    }

    private static string MapEventTypeLabel(ActivityEventType t) =>
        t switch
        {
            ActivityEventType.HabitPlus => "Habit +",
            ActivityEventType.HabitMinus => "Habit −",
            ActivityEventType.DailyComplete => "Daily done",
            ActivityEventType.DailyUncomplete => "Daily undone",
            ActivityEventType.TodoComplete => "To-do done",
            ActivityEventType.TodoUncomplete => "To-do undone",
            ActivityEventType.TimerSession => "Focus session",
            _ => t.ToString(),
        };

    private static DateOnly StartOfIsoWeek(DateOnly d)
    {
        int mondayOffset = ((int)d.DayOfWeek + 6) % 7;
        return d.AddDays(-mondayOffset);
    }

    private static int IntensityFor(int count, int maxInRange)
    {
        if (count <= 0 || maxInRange <= 0)
        {
            return 0;
        }

        if (count == 1)
        {
            return 1;
        }

        if (count <= 3)
        {
            return 2;
        }

        if (count <= 5)
        {
            return 3;
        }

        return 4;
    }
}
