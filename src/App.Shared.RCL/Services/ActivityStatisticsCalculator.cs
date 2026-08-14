using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>Shared period-resolution parameters for the contributions views.</summary>
public sealed record ContributionsRangeContext(
    string PeriodKey,
    IReadOnlyList<DailyGraphPeriodOption> Options,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    DateOnly TodayCutoff,
    IUserTimeZoneService? TimeZone = null,
    TimeSpan? DayStartLocalTime = null);

/// <summary>Shared aggregation logic for activity statistics, used by the web DB and the MAUI local store.</summary>
public static class ActivityStatisticsCalculator
{
    private const int HeatmapDataDays = 370;

    public static IReadOnlyList<DailyGraphPeriodOption> BuildPeriodOptions(
        DateOnly referenceToday,
        IReadOnlyList<UserActivityEventRecord> allUserEvents,
        IUserTimeZoneService? timeZone = null,
        TimeSpan? dayStartLocalTime = null)
    {
        var maxYear = referenceToday.Year;
        UserActivityEventRecord? first = allUserEvents
            .Where(e => e.EventType == ActivityEventType.DailyComplete)
            .OrderBy(e => e.OccurredAtUtc)
            .FirstOrDefault();

        var minYear = first is { } f
            ? DailySchedule.LocalDay(f.OccurredAtUtc, timeZone, dayStartLocalTime).Year
            : maxYear;
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
        var key = string.IsNullOrWhiteSpace(periodKey) || !optionKeys.Contains(periodKey)
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
        DateOnly todayCutoff,
        IUserTimeZoneService? timeZone = null,
        TimeSpan? dayStartLocalTime = null)
    {
        var perDay = PopulatePerDayCounts(rows, timeZone, dayStartLocalTime);
        var (maxDayCount, busiestDay) = FindBusiestDay(perDay);

        var totalEvents = rows.Count;
        var totalFocusSec = rows
            .Where(x => x.EventType == ActivityEventType.TimerSession)
            .Sum(x => x.DurationSeconds.GetValueOrDefault());
        var totalFocusMinutes = FocusMinutes(totalFocusSec);

        var actualEnd = ClampEndToCutoff(end, todayCutoff);
        var startWeek = StartOfIsoWeek(start);
        var endWeek = StartOfIsoWeek(actualEnd);
        var weekSpan = (endWeek.DayNumber - startWeek.DayNumber) / 7;
        if (weekSpan < 0)
        {
            weekSpan = 0;
        }

        var weekCount = weekSpan + 1;
        var weekBars = BuildWeekBars(perDay, start, actualEnd, startWeek, weekCount);

        var weekBarsRangeStart = weekBars[0].WeekStart;
        var weekBarsRangeEnd = weekBars[^1].WeekStart.AddDays(6);

        var heatmapSpanDays = actualEnd.DayNumber - start.DayNumber + 1;
        if (heatmapSpanDays < 1)
        {
            heatmapSpanDays = 1;
        }

        var gridStartMonday = StartOfIsoWeek(start);
        var gridWeekColumns = WeekGridColumns(start, actualEnd);

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
        IReadOnlyList<UserActivityEventRecord> rows,
        IUserTimeZoneService? timeZone = null,
        TimeSpan? dayStartLocalTime = null)
    {
        var perDay = new Dictionary<DateOnly, (int count, int focusSec)>();
        var netDailyItemDay = BuildNetDailyItemDayMap(rows, timeZone, dayStartLocalTime);
        var netTodoItemDay = BuildNetToggleItemDayMap(
            rows,
            ActivityEventType.TodoComplete,
            ActivityEventType.TodoUncomplete,
            timeZone,
            dayStartLocalTime);

        foreach (var r in rows)
        {
            if (r.BoardItemId is { }
                && r.EventType is ActivityEventType.DailyComplete or ActivityEventType.DailyUncomplete
                    or ActivityEventType.TodoComplete or ActivityEventType.TodoUncomplete)
            {
                continue;
            }

            var d = DailySchedule.LocalDay(r.OccurredAtUtc, timeZone, dayStartLocalTime);
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

            weekBars.Add(new ActivityWeekBarDto(i, ws, ev, FocusMinutes(focus)));
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
        return BuildHeatmapCells(
            gridStartMonday,
            gridWeekColumns,
            start,
            end,
            todayCutoff,
            d => perDay.TryGetValue(d, out var t) ? t.count : 0,
            maxDayCount);
    }

    public static DailyContributionsViewDto BuildDailyContributions(
        IReadOnlyList<UserActivityEventRecord> eventRowsInRange,
        IReadOnlyList<DailyItemStatsDto> dailyItemRows,
        ContributionsRangeContext range)
    {
        if (dailyItemRows.Count == 0)
        {
            return new DailyContributionsViewDto(
                range.PeriodKey,
                range.Options,
                [],
                range.RangeStart,
                range.RangeEnd);
        }

        var dailyIds = dailyItemRows.Select(x => x.Id).ToHashSet();
        var byItem = BuildItemCompletionMap(eventRowsInRange, dailyIds, range.TimeZone, range.DayStartLocalTime);
        var (commonStart, commonEnd) = FindCommonDates(dailyItemRows, byItem, range.RangeStart, range.RangeEnd, range.TodayCutoff);
        var graphs = BuildDailyContributionGraphs(dailyItemRows, byItem, commonStart, commonEnd, range.TodayCutoff, range.Options);

        return new DailyContributionsViewDto(
            range.PeriodKey,
            range.Options,
            graphs,
            commonStart,
            commonEnd);
    }

    private static Dictionary<Guid, Dictionary<DateOnly, int>> BuildItemCompletionMap(
        IReadOnlyList<UserActivityEventRecord> eventRowsInRange,
        HashSet<Guid> dailyIds,
        IUserTimeZoneService? timeZone = null,
        TimeSpan? dayStartLocalTime = null)
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
                 (e.BoardItemId ?? Guid.Empty, DailySchedule.LocalDay(e.OccurredAtUtc, timeZone, dayStartLocalTime))))
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
        var commonEnd = ClampEndToCutoff(rangeEnd, todayCutoff);
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

        var commonStart = ClampCommonStart(earliestDailyStart, rangeStart, commonEnd);
        return (commonStart, commonEnd);
    }

    private static List<DailyContributionGraphDto> BuildDailyContributionGraphs(
        IReadOnlyList<DailyItemStatsDto> dailyItemRows,
        Dictionary<Guid, Dictionary<DateOnly, int>> byItem,
        DateOnly commonStart,
        DateOnly commonEnd,
        DateOnly todayCutoff,
        IReadOnlyList<DailyGraphPeriodOption> options)
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

            var columns = WeekGridColumns(commonStart, commonEnd);

            graphs.Add(new DailyContributionGraphDto(
                di.Id,
                di.Title,
                graphHeat,
                columns,
                maxInRange,
                AvailablePeriodKeys(di, options, todayCutoff)));
        }
        return graphs;
    }

    /// <summary>
    ///     Periods that can contain recorded data for an item: those whose range reaches the item's first scheduled
    ///     day. Periods ending before the item existed would only ever show an empty grid.
    /// </summary>
    private static List<string> AvailablePeriodKeys(
        DailyItemStatsDto item,
        IReadOnlyList<DailyGraphPeriodOption> options,
        DateOnly todayCutoff)
    {
        var dataStart = item.DailyStartDate ?? item.CreatedAt;
        return options
            .Where(o => ResolveActivityPeriod(o.Key, todayCutoff, options).End >= dataStart)
            .Select(o => o.Key)
            .ToList();
    }

    public static HabitContributionsViewDto BuildHabitContributions(
        IReadOnlyList<UserActivityEventRecord> eventRowsInRange,
        IReadOnlyList<HabitItemStatsDto> habitItemRows,
        ContributionsRangeContext range)
    {
        if (habitItemRows.Count == 0)
        {
            return new HabitContributionsViewDto(range.PeriodKey, range.Options, [], range.RangeStart, range.RangeEnd);
        }

        var habitIds = habitItemRows.Select(x => x.Id).ToHashSet();
        var byItem = BuildHabitEventDayMap(eventRowsInRange, habitIds, range.TimeZone, range.DayStartLocalTime);

        var commonEnd = ClampEndToCutoff(range.RangeEnd, range.TodayCutoff);
        var earliestCreated = habitItemRows.Min(x => x.CreatedAt);
        var commonStart = ClampCommonStart(earliestCreated, range.RangeStart, commonEnd);

        var graphs = BuildHabitContributionGraphs(habitItemRows, byItem, commonStart, commonEnd);

        return new HabitContributionsViewDto(
            range.PeriodKey,
            range.Options,
            graphs,
            commonStart,
            commonEnd);
    }

    private static Dictionary<Guid, Dictionary<DateOnly, int>> BuildHabitEventDayMap(
        IReadOnlyList<UserActivityEventRecord> eventRowsInRange,
        HashSet<Guid> habitIds,
        IUserTimeZoneService? timeZone = null,
        TimeSpan? dayStartLocalTime = null)
    {
        var byItem = new Dictionary<Guid, Dictionary<DateOnly, int>>();
        foreach (var e in eventRowsInRange)
        {
            if (e.BoardItemId is not { } id || !habitIds.Contains(id))
            {
                continue;
            }

            if (e.EventType is not (ActivityEventType.HabitPlus or ActivityEventType.HabitMinus))
            {
                continue;
            }

            var d = DailySchedule.LocalDay(e.OccurredAtUtc, timeZone, dayStartLocalTime);
            if (!byItem.TryGetValue(id, out var map))
            {
                map = [];
                byItem[id] = map;
            }

            map[d] = map.TryGetValue(d, out var c) ? c + 1 : 1;
        }

        return byItem;
    }

    private static List<HabitContributionGraphDto> BuildHabitContributionGraphs(
        IReadOnlyList<HabitItemStatsDto> habitItemRows,
        Dictionary<Guid, Dictionary<DateOnly, int>> byItem,
        DateOnly commonStart,
        DateOnly commonEnd)
    {
        List<HabitContributionGraphDto> graphs = [with(capacity: habitItemRows.Count)];
        foreach (var hi in habitItemRows)
        {
            _ = byItem.TryGetValue(hi.Id, out var countByDay);
            countByDay ??= [];

            var activeDays = 0;
            var maxInRange = 0;
            for (var d = commonStart; d <= commonEnd; d = d.AddDays(1))
            {
                if (!countByDay.TryGetValue(d, out var c))
                {
                    continue;
                }

                activeDays++;
                if (c > maxInRange)
                {
                    maxInRange = c;
                }
            }

            IReadOnlyList<ActivityHeatmapCellDto> graphHeat = BuildRangeContributionHeatmap(
                commonStart,
                commonEnd,
                commonEnd,
                countByDay,
                maxInRange);

            var columns = WeekGridColumns(commonStart, commonEnd);

            var periodDays = commonEnd.DayNumber - commonStart.DayNumber + 1;
            if (periodDays < 1)
            {
                periodDays = 1;
            }

            graphs.Add(new HabitContributionGraphDto(
                hi.Id,
                hi.Title,
                graphHeat,
                columns,
                activeDays,
                periodDays,
                maxInRange));
        }

        return graphs;
    }

    public static ActivityDayDetailDto BuildDayDetail(
        DateOnly day,
        IReadOnlyList<UserActivityEventRecord> rows,
        IReadOnlyDictionary<Guid, string> titles,
        IUserTimeZoneService? timeZone = null,
        TimeSpan? dayStartLocalTime = null)
    {
        var lastDailyToggle = BuildLastToggleItemDayMap(
            rows,
            ActivityEventType.DailyComplete,
            ActivityEventType.DailyUncomplete,
            timeZone,
            dayStartLocalTime);
        var lastTodoToggle = BuildLastToggleItemDayMap(
            rows,
            ActivityEventType.TodoComplete,
            ActivityEventType.TodoUncomplete,
            timeZone,
            dayStartLocalTime);

        List<ActivityDayEventDto> list = [with(capacity: rows.Count)];
        var focusSec = 0;
        foreach (var r in rows.OrderBy(x => x.OccurredAtUtc))
        {
            if (ShouldSkipEventRow(r, lastDailyToggle, lastTodoToggle, timeZone, dayStartLocalTime))
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

        var focusMinutesTotal = FocusMinutes(focusSec);
        return new ActivityDayDetailDto(day, list, focusMinutesTotal);
    }

    private static bool ShouldSkipEventRow(
        UserActivityEventRecord r,
        Dictionary<(Guid id, DateOnly d), UserActivityEventRecord> lastDailyToggle,
        Dictionary<(Guid id, DateOnly d), UserActivityEventRecord> lastTodoToggle,
        IUserTimeZoneService? timeZone = null,
        TimeSpan? dayStartLocalTime = null)
    {
        if (r.EventType is ActivityEventType.DailyUncomplete or ActivityEventType.TodoUncomplete)
        {
            return true;
        }

        if (r.BoardItemId is not { } boardId)
        {
            return r.EventType is ActivityEventType.DailyComplete or ActivityEventType.TodoComplete;
        }

        var utcDay = DailySchedule.LocalDay(r.OccurredAtUtc, timeZone, dayStartLocalTime);
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
        return BuildHeatmapCells(
            StartOfIsoWeek(rangeStart),
            WeekGridColumns(rangeStart, rangeEnd),
            rangeStart,
            rangeEnd,
            todayCutoff,
            d => countByDay.TryGetValue(d, out var n) ? n : 0,
            maxInRange);
    }

    private static List<ActivityHeatmapCellDto> BuildHeatmapCells(
        DateOnly gridStartMonday,
        int gridWeekColumns,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        DateOnly todayCutoff,
        Func<DateOnly, int> countFor,
        int maxCount)
    {
        List<ActivityHeatmapCellDto> heat = [with(capacity: gridWeekColumns * 7)];
        for (var c = 0; c < gridWeekColumns; c++)
        {
            for (var r = 0; r < 7; r++)
            {
                var date = gridStartMonday.AddDays((c * 7) + r);
                var inRange = date >= rangeStart && date <= rangeEnd && date <= todayCutoff;
                if (!inRange)
                {
                    heat.Add(new ActivityHeatmapCellDto(r, c, date, 0, 0, false));
                    continue;
                }

                var count = countFor(date);
                var intensity = IntensityFor(count, maxCount);
                heat.Add(new ActivityHeatmapCellDto(r, c, date, count, intensity, true));
            }
        }

        return heat;
    }

    /// <summary>
    ///     For each board item and local calendar day pair, the last event wins: the daily is "done" for that day in stats
    ///     only if the last complete/uncomplete for that pair is <paramref name="completeType" />.
    ///     Events are grouped by the user's local day, matching the schedule the board runs on.
    /// </summary>
    private static Dictionary<(Guid id, DateOnly d), bool> BuildNetToggleItemDayMap(
        IReadOnlyList<UserActivityEventRecord> rows,
        ActivityEventType completeType,
        ActivityEventType uncompleteType,
        IUserTimeZoneService? timeZone = null,
        TimeSpan? dayStartLocalTime = null)
    {
        var lastByKey = BuildLastToggleItemDayMap(rows, completeType, uncompleteType, timeZone, dayStartLocalTime);
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
        IReadOnlyList<UserActivityEventRecord> rows,
        IUserTimeZoneService? timeZone = null,
        TimeSpan? dayStartLocalTime = null)
    {
        return BuildNetToggleItemDayMap(
            rows,
            ActivityEventType.DailyComplete,
            ActivityEventType.DailyUncomplete,
            timeZone,
            dayStartLocalTime);
    }

    private static Dictionary<(Guid id, DateOnly d), UserActivityEventRecord> BuildLastToggleItemDayMap(
        IReadOnlyList<UserActivityEventRecord> rows,
        ActivityEventType completeType,
        ActivityEventType uncompleteType,
        IUserTimeZoneService? timeZone = null,
        TimeSpan? dayStartLocalTime = null)
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

            var d = DailySchedule.LocalDay(r.OccurredAtUtc, timeZone, dayStartLocalTime);
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

    private static int WeekGridColumns(DateOnly start, DateOnly end)
    {
        var startMonday = StartOfIsoWeek(start);
        var endMonday = StartOfIsoWeek(end);
        var columns = ((endMonday.DayNumber - startMonday.DayNumber) / 7) + 1;
        return columns < 1 ? 1 : columns;
    }

    private static int FocusMinutes(int totalSeconds) => (totalSeconds + 30) / 60;

    private static DateOnly ClampEndToCutoff(DateOnly rangeEnd, DateOnly todayCutoff) =>
        rangeEnd > todayCutoff ? todayCutoff : rangeEnd;

    private static DateOnly ClampCommonStart(DateOnly earliest, DateOnly rangeStart, DateOnly commonEnd)
    {
        var start = earliest < rangeStart ? rangeStart : earliest;
        return start > commonEnd ? commonEnd : start;
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
