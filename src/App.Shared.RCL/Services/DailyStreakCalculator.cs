using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>
///     Consecutive daily streak from UTC completion history (per calendar day) and the daily schedule.
/// </summary>
public static class DailyStreakCalculator
{
    public const int MaxStreak = DailySchedule.MaxHistoryDays;

    /// <summary>UTC instant used when logging a backdated check so the day matches <paramref name="day" />.</summary>
    public static DateTimeOffset BackdatedDailyEventOccurredAt(DateOnly day) =>
        new(day.Year, day.Month, day.Day, 15, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     For each day, completion is: last <see cref="ActivityEventType.DailyComplete" /> or
    ///     <see cref="ActivityEventType.DailyUncomplete" /> for that day wins. A day with no such events
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
        return source
            .Where(e => e.Type is ActivityEventType.DailyComplete or ActivityEventType.DailyUncomplete)
            .GroupBy(e => DateOnly.FromDateTime(e.OccurredAtUtc.UtcDateTime))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e => e.OccurredAtUtc).Select(e => (e.OccurredAtUtc, e.Type)).ToList()
            );
    }

    /// <summary>
    ///     Consecutive completed <em>scheduled</em> days counting backward. <paramref name="today" /> is included
    ///     only when it is completed for this daily (events or <paramref name="dailyLastCompletedOn" />).
    ///     otherwise the chain ends at the previous calendar day, so a not-yet-checked-off today does not
    ///     add to the streak.
    /// </summary>
    /// <remarks>
    ///     A <c>null</c> <paramref name="dailyStart" /> means the board treats the daily as due from today,
    ///     every calendar day is scheduled, so the walk must not apply the repeat pattern which would
    ///     otherwise count only every N-th day and show streaks stuck near zero for interval dailies.
    /// </remarks>
    public static int ComputeStreak(
        DateOnly? dailyStart,
        DailyRepeatType repeat,
        int repeatInterval,
        DateOnly today,
        IReadOnlyDictionary<DateOnly, List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>> eventsByDay,
        DateOnly? dailyLastCompletedOn)
    {
        var effectiveRepeat = dailyStart is null ? DailyRepeatType.Daily : repeat;
        var effectiveInterval = dailyStart is null ? 1 : repeatInterval;

        var todayOnSchedule = DailySchedule.IsScheduledOn(dailyStart, effectiveRepeat, effectiveInterval, today);
        var todayDone = todayOnSchedule &&
            IsCalendarDayNetCompleted(today, GetDayListOrNull(eventsByDay, today), dailyLastCompletedOn);

        // Do not count today until it is done. Count only through prior days when it is not.
        var end = todayDone ? today : today.AddDays(-1);

        var historyStart =
            DailySchedule.StreakHistoryScheduleStart(dailyStart, end, effectiveRepeat, effectiveInterval, MaxStreak);

        var n = 0;
        foreach (var d in DailySchedule.WalkScheduledDaysBackward(end, historyStart, effectiveRepeat, effectiveInterval))
        {
            if (!IsCalendarDayNetCompleted(d, GetDayListOrNull(eventsByDay, d), dailyLastCompletedOn))
            {
                break;
            }

            n++;
        }

        return Math.Min(MaxStreak, n);
    }

    private static List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>? GetDayListOrNull(
        IReadOnlyDictionary<DateOnly, List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>> eventsByDay,
        DateOnly day) =>
        eventsByDay.TryGetValue(day, out var list) ? list : null;
}
