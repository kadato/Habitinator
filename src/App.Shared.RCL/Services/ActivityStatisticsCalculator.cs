using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>Shared aggregation logic for activity statistics (web DB and MAUI local store).</summary>
public static class ActivityStatisticsCalculator
{
    private const int HeatmapDataDays = 370;

    public static IReadOnlyList<DailyGraphPeriodOption> BuildPeriodOptions(
        DateOnly referenceToday,
        IReadOnlyList<UserActivityEventRecord> allUserEvents)
    {
        var maxYear = referenceToday.Year;
        UserActivityEventRecord? first = allUserEvents
            .Where(e => e.EventType == ActivityEventType.DailyComplete)
            .OrderBy(e => e.OccurredAtUtc)
            .FirstOrDefault();

        var minYear = first is { } f ? f.OccurredAtUtc.UtcDateTime.Year : maxYear;
        if (minYear > maxYear)
        {
            minYear = maxYear;
        }

        var list = new List<DailyGraphPeriodOption>
        {
            new(DailyGraphPeriods.Rolling370Days, $"Last {HeatmapDataDays} days")
        };
        for (var y = maxYear; y >= minYear; y--)
        {
            list.Add(new DailyGraphPeriodOption(DailyGraphPeriods.ForCalendarYear(y), y.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return list;
    }

    public static (string Key, DateOnly Start, DateOnly End) ResolveActivityPeriod(
        string? periodKey,
        DateOnly referenceToday,
        IReadOnlyList<DailyGraphPeriodOption> options)
    {
        HashSet<string> optionKeys = [.. options.Select(o => o.Key)];
        string key = string.IsNullOrWhiteSpace(periodKey) || !optionKeys.Contains(periodKey)
            ? DailyGraphPeriods.Rolling370Days
            : periodKey;

        if (string.Equals(key, DailyGraphPeriods.Rolling370Days, StringComparison.Ordinal))
        {
            var rangeEnd = referenceToday;
            var rangeStart = rangeEnd.AddDays(-(HeatmapDataDays - 1));
            return (key, rangeStart, rangeEnd);
        }

        if (key.StartsWith(DailyGraphPeriods.YearPrefix, StringComparison.Ordinal) &&
            int.TryParse(key.AsSpan(DailyGraphPeriods.YearPrefix.Length), out var y))
        {
            return (key, new DateOnly(y, 1, 1), new DateOnly(y, 12, 31));
        }

        return (DailyGraphPeriods.Rolling370Days, referenceToday.AddDays(-(HeatmapDataDays - 1)), referenceToday);
    }

    public static ActivityDashboardDto BuildDashboard(
        IReadOnlyList<UserActivityEventRecord> rows,
        string periodKey,
        DateOnly start,
        DateOnly end,
        DateOnly todayCutoff)
    {
        var perDay = PopulatePerDayCounts(rows);
        var (maxDayCount, busiestDay) = FindBusiestDay(perDay);

        var totalEvents = rows.Count;
        var totalFocusSec = rows
            .Where(x => x.EventType == ActivityEventType.TimerSession)
            .Sum(x => x.DurationSeconds.GetValueOrDefault());
        var totalFocusMinutes = (totalFocusSec + 30) / 60;

        var actualEnd = end > todayCutoff ? todayCutoff : end;
        var startWeek = StartOfIsoWeek(start);
        var endWeek = StartOfIsoWeek(actualEnd);
        var weekSpan = (endWeek.DayNumber - startWeek.DayNumber) / 7;
        if (weekSpan < 0)
        {
            weekSpan = 0;
        }

        var weekCount = weekSpan + 1;
        var weekBars = BuildWeekBars(perDay, start, actualEnd, startWeek, weekCount);

        var weekBarsRangeStart = weekCount > 0 ? weekBars[0].WeekStart : startWeek;
        var weekBarsRangeEnd = weekCount > 0 ? weekBars[^1].WeekStart.AddDays(6) : actualEnd;

        var heatmapSpanDays = actualEnd.DayNumber - start.DayNumber + 1;
        if (heatmapSpanDays < 1)
        {
            heatmapSpanDays = 1;
        }

        var gridStartMonday = StartOfIsoWeek(start);
        var endMonday = StartOfIsoWeek(actualEnd);
        var gridWeekColumns = ((endMonday.DayNumber - gridStartMonday.DayNumber) / 7) + 1;
        if (gridWeekColumns < 1)
        {
            gridWeekColumns = 1;
        }

        var heat = BuildHeatmapGrid(perDay, start, end, todayCutoff, gridStartMonday, gridWeekColumns, maxDayCount);

        return new ActivityDashboardDto(
            periodKey,
            weekBars,
            heat,
            gridWeekColumns,
            totalEvents,
            totalFocusMinutes,
            maxDayCount,
            busiestDay,
            start,
            actualEnd,
            heatmapSpanDays,
            weekBarsRangeStart,
            weekBarsRangeEnd,
            []);
    }

    private static Dictionary<DateOnly, (int count, int focusSec)> PopulatePerDayCounts(
        IReadOnlyList<UserActivityEventRecord> rows)
    {
        var perDay = new Dictionary<DateOnly, (int count, int focusSec)>();
        var netDailyItemDay = BuildNetDailyItemDayMap(rows);
        var netTodoItemDay = BuildNetToggleItemDayMap(
            rows,
            ActivityEventType.TodoComplete,
            ActivityEventType.TodoUncomplete);

        foreach (var r in rows)
        {
            if (r.BoardItemId is { }
                && r.EventType is ActivityEventType.DailyComplete or ActivityEventType.DailyUncomplete
                    or ActivityEventType.TodoComplete or ActivityEventType.TodoUncomplete)
            {
                continue;
            }

            var d = DateOnly.FromDateTime(r.OccurredAtUtc.UtcDateTime);
            if (!perDay.TryGetValue(d, out var acc))
            {
                acc = (0, 0);
            }

            var focus = 0;
            if (r.EventType == ActivityEventType.TimerSession && r.DurationSeconds is int s)
            {
                focus = s;
            }
            perDay[d] = (acc.count + 1, acc.focusSec + focus);
        }

        ApplyNetToggleCountsToPerDay(perDay, netDailyItemDay);
        ApplyNetToggleCountsToPerDay(perDay, netTodoItemDay);
        return perDay;
    }

    private static (int maxDayCount, DateOnly? busiestDay) FindBusiestDay(
        Dictionary<DateOnly, (int count, int focusSec)> perDay)
    {
        var maxDayCount = 0;
        DateOnly? busiestDay = null;
        foreach (var kv in perDay)
        {
            if (kv.Value.count > maxDayCount)
            {
                maxDayCount = kv.Value.count;
                busiestDay = kv.Key;
            }
            else if (kv.Value.count == maxDayCount && maxDayCount > 0 && kv.Key > busiestDay)
            {
                busiestDay = kv.Key;
            }
        }
        return (maxDayCount, busiestDay);
    }

    private static List<ActivityWeekBarDto> BuildWeekBars(
        Dictionary<DateOnly, (int count, int focusSec)> perDay,
        DateOnly start,
        DateOnly actualEnd,
        DateOnly startWeek,
        int weekCount)
    {
        List<ActivityWeekBarDto> weekBars = [with(capacity: weekCount)];
        for (var i = 0; i < weekCount; i++)
        {
            var ws = startWeek.AddDays(i * 7);
            var we = ws.AddDays(6);
            var clipFrom = ws < start ? start : ws;
            var clipTo = we > actualEnd ? actualEnd : we;

            if (clipFrom > clipTo)
            {
                weekBars.Add(new ActivityWeekBarDto(i, ws, 0, 0));
                continue;
            }

            var ev = 0;
            var focus = 0;
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
        return weekBars;
    }

    private static List<ActivityHeatmapCellDto> BuildHeatmapGrid(
        Dictionary<DateOnly, (int count, int focusSec)> perDay,
        DateOnly start,
        DateOnly end,
        DateOnly todayCutoff,
        DateOnly gridStartMonday,
        int gridWeekColumns,
        int maxDayCount)
    {
        List<ActivityHeatmapCellDto> heat = [with(capacity: gridWeekColumns * 7)];
        for (var c = 0; c < gridWeekColumns; c++)
        {
            for (var r = 0; r < 7; r++)
            {
                var date = gridStartMonday.AddDays((c * 7) + r);
                var inRange = date >= start && date <= end && date <= todayCutoff;
                if (!inRange)
                {
                    heat.Add(new ActivityHeatmapCellDto(r, c, date, 0, 0, false));
                }
                else
                {
                    var count = perDay.TryGetValue(date, out var t) ? t.count : 0;
                    var intensity = IntensityFor(count, maxDayCount);
                    heat.Add(new ActivityHeatmapCellDto(r, c, date, count, intensity, true));
                }
            }
        }
        return heat;
    }

    public static DailyContributionsViewDto BuildDailyContributions(
        IReadOnlyList<UserActivityEventRecord> eventRowsInRange,
        IReadOnlyList<DailyItemStatsDto> dailyItemRows,
        string periodKey,
        IReadOnlyList<DailyGraphPeriodOption> options,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        DateOnly todayCutoff)
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
        var byItem = BuildItemCompletionMap(eventRowsInRange, dailyIds);
        var (commonStart, commonEnd) = FindCommonDates(dailyItemRows, byItem, rangeStart, rangeEnd, todayCutoff);
        var graphs = BuildDailyContributionGraphs(dailyItemRows, byItem, commonStart, commonEnd, todayCutoff);

        return new DailyContributionsViewDto(
            periodKey,
            options,
            graphs,
            commonStart,
            commonEnd);
    }

    private static Dictionary<Guid, Dictionary<DateOnly, int>> BuildItemCompletionMap(
        IReadOnlyList<UserActivityEventRecord> eventRowsInRange,
        HashSet<Guid> dailyIds)
    {
        var inRange = eventRowsInRange
            .Where(e =>
                e.BoardItemId is { } bid &&
                dailyIds.Contains(bid) &&
                (e.EventType == ActivityEventType.DailyComplete ||
                 e.EventType == ActivityEventType.DailyUncomplete))
            .ToList();

        var byItem = new Dictionary<Guid, Dictionary<DateOnly, int>>();
        var netByItemDay = new Dictionary<(Guid id, DateOnly d), bool>();
        foreach (var g in inRange.GroupBy(e =>
                 (e.BoardItemId!.Value, DateOnly.FromDateTime(e.OccurredAtUtc.UtcDateTime))))
        {
            var last = g.OrderBy(e => e.OccurredAtUtc).Last();
            netByItemDay[g.Key] = last.EventType == ActivityEventType.DailyComplete;
        }

        foreach (var ((id, d), isDone) in netByItemDay)
        {
            if (!isDone)
            {
                continue;
            }

            if (!byItem.TryGetValue(id, out var map))
            {
                map = [];
                byItem[id] = map;
            }

            map[d] = 1;
        }
        return byItem;
    }

    private static (DateOnly commonStart, DateOnly commonEnd) FindCommonDates(
        IReadOnlyList<DailyItemStatsDto> dailyItemRows,
        Dictionary<Guid, Dictionary<DateOnly, int>> byItem,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        DateOnly todayCutoff)
    {
        var commonEnd = rangeEnd > todayCutoff ? todayCutoff : rangeEnd;
        var earliestDailyStart = commonEnd; // Default
        foreach (var di in dailyItemRows)
        {
            var dailyStart = di.DailyStartDate ?? di.CreatedAt;
            _ = byItem.TryGetValue(di.Id, out var countByDay);
            if (countByDay is { Keys.Count: > 0 })
            {
                var firstCompletion = countByDay.Keys.Min();
                if (firstCompletion < dailyStart)
                {
                    dailyStart = firstCompletion;
                }
            }

            if (dailyStart < earliestDailyStart)
            {
                earliestDailyStart = dailyStart;
            }
        }

        var commonStart = earliestDailyStart < rangeStart ? rangeStart : earliestDailyStart;
        if (commonStart > commonEnd)
        {
            commonStart = commonEnd;
        }
        return (commonStart, commonEnd);
    }

    private static List<DailyContributionGraphDto> BuildDailyContributionGraphs(
        IReadOnlyList<DailyItemStatsDto> dailyItemRows,
        Dictionary<Guid, Dictionary<DateOnly, int>> byItem,
        DateOnly commonStart,
        DateOnly commonEnd,
        DateOnly todayCutoff)
    {
        List<DailyContributionGraphDto> graphs = [with(capacity: dailyItemRows.Count)];
        foreach (var di in dailyItemRows)
        {
            _ = byItem.TryGetValue(di.Id, out var countByDay);
            countByDay ??= [];

            var maxInRange = 0;
            for (var d = commonStart; d <= commonEnd; d = d.AddDays(1))
            {
                if (countByDay.TryGetValue(d, out var c) && c > maxInRange)
                {
                    maxInRange = c;
                }
            }

            IReadOnlyList<ActivityHeatmapCellDto> graphHeat = BuildRangeContributionHeatmap(
                commonStart,
                commonEnd,
                todayCutoff,
                countByDay,
                maxInRange);

            var gStartW = StartOfIsoWeek(commonStart);
            var gEndW = StartOfIsoWeek(commonEnd);
            var columns = ((gEndW.DayNumber - gStartW.DayNumber) / 7) + 1;
            if (columns < 1)
            {
                columns = 1;
            }

            graphs.Add(new DailyContributionGraphDto(di.Id, di.Title, graphHeat, columns, maxInRange));
        }
        return graphs;
    }

    public static ActivityDayDetailDto BuildDayDetail(
        DateOnly day,
        IReadOnlyList<UserActivityEventRecord> rows,
        IReadOnlyDictionary<Guid, string> titles)
    {
        var lastDailyToggle = BuildLastToggleItemDayMap(
            rows,
            ActivityEventType.DailyComplete,
            ActivityEventType.DailyUncomplete);
        var lastTodoToggle = BuildLastToggleItemDayMap(
            rows,
            ActivityEventType.TodoComplete,
            ActivityEventType.TodoUncomplete);

        List<ActivityDayEventDto> list = [with(capacity: rows.Count)];
        var focusSec = 0;
        foreach (var r in rows.OrderBy(x => x.OccurredAtUtc))
        {
            if (ShouldSkipEventRow(r, lastDailyToggle, lastTodoToggle))
            {
                continue;
            }

            var itemTitle = r.BoardItemId is { } id ? titles.GetValueOrDefault(id) : null;
            list.Add(new ActivityDayEventDto(
                r.OccurredAtUtc,
                r.EventType,
                FormatDayDetailEventLabel(r.EventType, itemTitle, r.CustomLabel),
                itemTitle,
                r.DurationSeconds,
                r.CustomLabel));

            if (r is { EventType: ActivityEventType.TimerSession, DurationSeconds: int s })
            {
                focusSec += s;
            }
        }

        var focusMinutesTotal = (focusSec + 30) / 60;
        return new ActivityDayDetailDto(day, list, focusMinutesTotal);
    }

    private static bool ShouldSkipEventRow(
        UserActivityEventRecord r,
        Dictionary<(Guid id, DateOnly d), UserActivityEventRecord> lastDailyToggle,
        Dictionary<(Guid id, DateOnly d), UserActivityEventRecord> lastTodoToggle)
    {
        if (r.EventType is ActivityEventType.DailyUncomplete or ActivityEventType.TodoUncomplete)
        {
            return true;
        }

        if (r.BoardItemId is not { } boardId)
        {
            return r.EventType is ActivityEventType.DailyComplete or ActivityEventType.TodoComplete;
        }

        var utcDay = DateOnly.FromDateTime(r.OccurredAtUtc.UtcDateTime);
        return r.EventType == ActivityEventType.DailyComplete
            ? !ShouldIncludeNetToggleRow(r, lastDailyToggle, boardId, utcDay, ActivityEventType.DailyComplete)
            : r.EventType == ActivityEventType.TodoComplete && !ShouldIncludeNetToggleRow(r, lastTodoToggle, boardId, utcDay, ActivityEventType.TodoComplete);
    }

    private static bool ShouldIncludeNetToggleRow(
        UserActivityEventRecord row,
        Dictionary<(Guid id, DateOnly d), UserActivityEventRecord> lastToggleByItemDay,
        Guid boardId,
        DateOnly utcDay,
        ActivityEventType completeType)
    {
        return lastToggleByItemDay.TryGetValue((boardId, utcDay), out var last) && last.OccurredAtUtc == row.OccurredAtUtc &&
               last.EventType == row.EventType &&
               last.EventType == completeType;
    }

    private static List<ActivityHeatmapCellDto> BuildRangeContributionHeatmap(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        DateOnly todayCutoff,
        Dictionary<DateOnly, int> countByDay,
        int maxInRange)
    {
        var gridStartMonday = StartOfIsoWeek(rangeStart);
        var endMonday = StartOfIsoWeek(rangeEnd);
        var gridWeekColumns = ((endMonday.DayNumber - gridStartMonday.DayNumber) / 7) + 1;
        if (gridWeekColumns < 1)
        {
            gridWeekColumns = 1;
        }

        List<ActivityHeatmapCellDto> heat = [with(capacity: gridWeekColumns * 7)];
        for (var c = 0; c < gridWeekColumns; c++)
        {
            for (var r = 0; r < 7; r++)
            {
                var date = gridStartMonday.AddDays((c * 7) + r);
                var inWindow = date >= rangeStart && date <= rangeEnd;
                if (!inWindow)
                {
                    heat.Add(new ActivityHeatmapCellDto(r, c, date, 0, 0, false));
                    continue;
                }

                if (date > todayCutoff)
                {
                    heat.Add(new ActivityHeatmapCellDto(r, c, date, 0, 0, false));
                    continue;
                }

                var count = countByDay.TryGetValue(date, out var n) ? n : 0;
                var intensity = IntensityFor(count, maxInRange);
                heat.Add(new ActivityHeatmapCellDto(r, c, date, count, intensity, true));
            }
        }

        return heat;
    }

    /// <summary>
    ///     For each (board item, UTC calendar day), last event wins: daily is "done" for that day in stats
    ///     only if the last complete/uncomplete for that pair is <paramref name="completeType" />.
    ///     Events are stored and grouped by UTC calendar day.
    /// </summary>
    private static Dictionary<(Guid id, DateOnly d), bool> BuildNetToggleItemDayMap(
        IReadOnlyList<UserActivityEventRecord> rows,
        ActivityEventType completeType,
        ActivityEventType uncompleteType)
    {
        var lastByKey = BuildLastToggleItemDayMap(rows, completeType, uncompleteType);
        var result = new Dictionary<(Guid id, DateOnly d), bool>(lastByKey.Count);
        foreach (var (key, last) in lastByKey)
        {
            result[key] = last.EventType == completeType;
        }

        return result;
    }

    private static void ApplyNetToggleCountsToPerDay(
        Dictionary<DateOnly, (int count, int focusSec)> perDay,
        IReadOnlyDictionary<(Guid id, DateOnly d), bool> netItemDay)
    {
        foreach (var kvp in netItemDay)
        {
            if (!kvp.Value)
            {
                continue;
            }

            var d = kvp.Key.d;
            if (!perDay.TryGetValue(d, out var acc))
            {
                acc = (0, 0);
            }

            perDay[d] = (acc.count + 1, acc.focusSec);
        }
    }

    private static Dictionary<(Guid id, DateOnly d), bool> BuildNetDailyItemDayMap(
        IReadOnlyList<UserActivityEventRecord> rows)
    {
        return BuildNetToggleItemDayMap(
            rows,
            ActivityEventType.DailyComplete,
            ActivityEventType.DailyUncomplete);
    }

    private static Dictionary<(Guid id, DateOnly d), UserActivityEventRecord> BuildLastToggleItemDayMap(
        IReadOnlyList<UserActivityEventRecord> rows,
        ActivityEventType completeType,
        ActivityEventType uncompleteType)
    {
        var byKey = new Dictionary<(Guid id, DateOnly d), List<UserActivityEventRecord>>();
        foreach (var r in rows)
        {
            if (r.BoardItemId is not { } id)
            {
                continue;
            }

            if (r.EventType != completeType && r.EventType != uncompleteType)
            {
                continue;
            }

            var d = DateOnly.FromDateTime(r.OccurredAtUtc.UtcDateTime);
            var key = (id, d);
            if (!byKey.TryGetValue(key, out var list))
            {
                list = [];
                byKey[key] = list;
            }

            list.Add(r);
        }

        var result = new Dictionary<(Guid id, DateOnly d), UserActivityEventRecord>(byKey.Count);
        foreach (var (key, list) in byKey)
        {
            result[key] = list.OrderBy(x => x.OccurredAtUtc).Last();
        }

        return result;
    }

    public static string FormatDayDetailEventLabel(
        ActivityEventType eventType,
        string? itemTitle,
        string? customLabel)
    {
        var name = itemTitle ?? customLabel;
        return eventType switch
        {
            ActivityEventType.DailyComplete => name is not null ? $"Completed daily: {name}" : "Completed daily",
            ActivityEventType.DailyUncomplete => name ?? eventType.ToString(),
            ActivityEventType.TodoComplete => name is not null ? $"Completed to-do: {name}" : "Completed to-do",
            ActivityEventType.TodoUncomplete => name ?? eventType.ToString(),
            ActivityEventType.HabitPlus => name is not null ? $"Habit +: {name}" : "Habit +",
            ActivityEventType.HabitMinus => name is not null ? $"Habit −: {name}" : "Habit −",
            ActivityEventType.TimerSession => name is not null ? $"Focus session: {name}" : "Focus session",
            _ => name ?? eventType.ToString()
        };
    }

    private static DateOnly StartOfIsoWeek(DateOnly d)
    {
        var mondayOffset = ((int)d.DayOfWeek + 6) % 7;
        return d.AddDays(-mondayOffset);
    }

    private static int IntensityFor(int count, int maxInRange)
    {
        return count > 0 && maxInRange > 0
            ? count switch
            {
                1 => 1,
                <= 3 => 2,
                <= 5 => 3,
                _ => 4
            }
            : 0;
    }
}
