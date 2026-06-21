using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>
///     Consecutive daily streak from UTC completion history (per calendar day) and the daily schedule.
/// </summary>
public static class DailyStreakCalculator
{
    public const int MaxStreak = 9999;

    /// <summary>UTC instant used when logging a backdated check so the day matches <paramref name="day" />.</summary>
    public static DateTimeOffset BackdatedDailyEventOccurredAt(DateOnly day) =>
        new(day.Year, day.Month, day.Day, 15, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     For each day, completion is: last <see cref="ActivityEventType.DailyComplete" /> or
    ///     <see cref="ActivityEventType.DailyUncomplete" /> for that day wins; a day with no such events
    ///     is completed only if it equals <paramref name="dailyLastCompletedOn" />.
    /// </summary>
    public static bool IsCalendarDayNetCompleted(
        DateOnly d,
        IReadOnlyList<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>? eventsOnDay,
        DateOnly? dailyLastCompletedOn) =>
        eventsOnDay is { Count: > 0 }
            ? eventsOnDay[^1].Type == ActivityEventType.DailyComplete
            : dailyLastCompletedOn == d;

    /// <summary>Groups events by calendar day (UTC) and only DailyComplete / DailyUncomplete.</summary>
    public static Dictionary<DateOnly, List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>> GroupDailyEventsByUtcDay(
        IEnumerable<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)> source)
    {
        var map = new Dictionary<DateOnly, List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>>();
        foreach (var (occurred, type) in source)
        {
            if (type is not (ActivityEventType.DailyComplete or ActivityEventType.DailyUncomplete))
            {
                continue;
            }

            var d = DateOnly.FromDateTime(occurred.UtcDateTime);
            if (!map.TryGetValue(d, out var list))
            {
                list = new List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>();
                map[d] = list;
            }

            list.Add((occurred, type));
        }

        foreach (var list in map.Values)
        {
            list.Sort((a, b) => a.OccurredAtUtc.CompareTo(b.OccurredAtUtc));
        }

        return map;
    }

    /// <summary>
    ///     Consecutive completed <em>scheduled</em> days counting backward. <paramref name="today" /> is included
    ///     only when it is completed for this daily (events or <paramref name="dailyLastCompletedOn" />);
    ///     otherwise the chain ends at the previous calendar day, so a not-yet-checked-off today does not
    ///     add to the streak.
    /// </summary>
    public static int ComputeStreak(
        DateOnly? dailyStart,
        DailyRepeatType repeat,
        int repeatInterval,
        DateOnly today,
        IReadOnlyDictionary<DateOnly, List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>> eventsByDay,
        DateOnly? dailyLastCompletedOn)
    {
        var todayOnSchedule = DailySchedule.IsScheduledOn(dailyStart, repeat, repeatInterval, today);
        var todayDone = todayOnSchedule &&
            IsCalendarDayNetCompleted(today, GetDayListOrNull(eventsByDay, today), dailyLastCompletedOn);

        // Do not count today until it is done; count only through prior days when it is not.
        var end = todayDone ? today : today.AddDays(-1);

        var historyStart =
            DailySchedule.StreakHistoryScheduleStart(dailyStart, end, repeat, repeatInterval, MaxStreak);

        var n = 0;
        var d = end;
        var maxSteps = 20_000;
        while (maxSteps-- > 0)
        {
            if (d < historyStart)
            {
                break;
            }

            if (!DailySchedule.IsScheduledOn(historyStart, repeat, repeatInterval, d))
            {
                d = d.AddDays(-1);
                continue;
            }

            if (!IsCalendarDayNetCompleted(d, GetDayListOrNull(eventsByDay, d), dailyLastCompletedOn))
            {
                break;
            }

            n++;
            d = d.AddDays(-1);
        }

        return Math.Min(MaxStreak, n);
    }

    private static List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>? GetDayListOrNull(
        IReadOnlyDictionary<DateOnly, List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>> eventsByDay,
        DateOnly day) =>
        eventsByDay.TryGetValue(day, out var list) ? list : null;
}
