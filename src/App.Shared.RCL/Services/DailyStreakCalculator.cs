using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>The current streak, the previous streak, and the longest streak for one daily.</summary>
public sealed record DailyStreakDetails(int Current, int Previous, int Longest);

/// <summary>
///     Consecutive daily streak from UTC completion history, per calendar day, and the daily schedule.
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
    ///     A trailing <see cref="ActivityEventType.DailySkip" /> marks the day skipped.
    ///     The skipped day stays neutral for streaks.
    /// </summary>
    public static bool IsCalendarDayNetCompleted(
        DateOnly d,
        IReadOnlyList<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>? eventsOnDay,
        DateOnly? dailyLastCompletedOn) =>
        eventsOnDay is { Count: > 0 }
            ? eventsOnDay[^1].Type == ActivityEventType.DailyComplete
            : dailyLastCompletedOn == d;

    public static bool IsCalendarDaySkipped(
        IReadOnlyList<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>? eventsOnDay) =>
        eventsOnDay is { Count: > 0 } && eventsOnDay[^1].Type == ActivityEventType.DailySkip;

    /// <summary>
    ///     Groups events by the user's local calendar day, applying the same timezone and day-start
    ///     rollback as <see cref="DailySchedule.LocalToday" />. Streak walks schedule in local days, so
    ///     grouping in UTC would move check-ins made near the day boundary to the neighboring day and
    ///     mask a missed day. With no timezone, events fall on their UTC day. Backdated check-ins use
    ///     the fixed <see cref="BackdatedDailyEventOccurredAt" /> hour, which always lands on the
    ///     target day regardless of timezone.
    /// </summary>
    public static Dictionary<DateOnly, List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>> GroupDailyEventsByLocalDay(
        IEnumerable<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)> source,
        IUserTimeZoneService? timeZone = null,
        TimeSpan? dayStartLocalTime = null)
    {
        return source
            .Where(e => e.Type is ActivityEventType.DailyComplete or ActivityEventType.DailyUncomplete or ActivityEventType.DailySkip)
            .GroupBy(e => DailySchedule.LocalDay(e.OccurredAtUtc, timeZone, dayStartLocalTime))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e => e.OccurredAtUtc).Select(e => (e.OccurredAtUtc, e.Type)).ToList()
            );
    }

    /// <summary>
    ///     Consecutive completed <em>scheduled</em> days counting backward. <paramref name="today" /> is included
    ///     only when it is completed for this daily, via events or <paramref name="dailyLastCompletedOn" />.
    ///     Otherwise the chain ends at the previous calendar day, so a not-yet-checked-off today does not
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
        DateOnly? dailyLastCompletedOn,
        int weekdays = 0)
    {
        var effectiveWeekdays = DailyWeekdays.Normalize(weekdays);
        var effectiveRepeat = dailyStart is null ? DailyRepeatType.Daily : repeat;
        var effectiveInterval = dailyStart is null ? 1 : repeatInterval;
        if (dailyStart is null)
        {
            effectiveWeekdays = DailyWeekdays.None;
        }

        var todayOnSchedule = DailySchedule.IsScheduledOn(dailyStart, effectiveRepeat, effectiveInterval, today, effectiveWeekdays);
        var todayDone = todayOnSchedule &&
            IsCalendarDayNetCompleted(today, GetDayListOrNull(eventsByDay, today), dailyLastCompletedOn);

        // Do not count today until today is done. When today is not done, count only through yesterday.
        var end = todayDone ? today : today.AddDays(-1);

        var historyStart =
            DailySchedule.StreakHistoryScheduleStart(dailyStart, end, effectiveRepeat, effectiveInterval, MaxStreak);

        var n = 0;
        foreach (var d in DailySchedule.WalkScheduledDaysBackward(end, historyStart, effectiveRepeat, effectiveInterval, DailySchedule.MaxScheduledStepCap, effectiveWeekdays))
        {
            var dayEvents = GetDayListOrNull(eventsByDay, d);
            if (IsCalendarDaySkipped(dayEvents))
            {
                // Skipped days keep the streak unbroken without adding to the streak.
                continue;
            }

            if (!IsCalendarDayNetCompleted(d, dayEvents, dailyLastCompletedOn))
            {
                break;
            }

            n++;
        }

        return Math.Min(MaxStreak, n);
    }

    /// <summary>
    ///     The current streak, the previous streak, and the longest streak over scheduled days.
    ///     Skipped days keep a streak unbroken without adding to the streak. This matches <see cref="ComputeStreak" />.
    ///     The current streak counts back from today. Today counts only when today is done.
    ///     The longest streak is the best streak so far.
    ///     The previous streak is the finished streak right before the current streak.
    ///     When no streak is active, the previous streak is the most recent finished streak.
    /// </summary>
    public static DailyStreakDetails ComputeStreakDetails(
        DateOnly? dailyStart,
        DailyRepeatType repeat,
        int repeatInterval,
        DateOnly today,
        IReadOnlyDictionary<DateOnly, List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>> eventsByDay,
        DateOnly? dailyLastCompletedOn,
        int weekdays = 0)
    {
        var effectiveWeekdays = DailyWeekdays.Normalize(weekdays);
        var effectiveRepeat = dailyStart is null ? DailyRepeatType.Daily : repeat;
        var effectiveInterval = dailyStart is null ? 1 : repeatInterval;
        if (dailyStart is null)
        {
            effectiveWeekdays = DailyWeekdays.None;
        }

        var todayOnSchedule = DailySchedule.IsScheduledOn(dailyStart, effectiveRepeat, effectiveInterval, today, effectiveWeekdays);
        var todayDone = todayOnSchedule &&
            IsCalendarDayNetCompleted(today, GetDayListOrNull(eventsByDay, today), dailyLastCompletedOn);

        // Do not count today until today is done. When today is not done, count only through yesterday.
        var end = todayDone ? today : today.AddDays(-1);

        var historyStart =
            DailySchedule.StreakHistoryScheduleStart(dailyStart, end, effectiveRepeat, effectiveInterval, MaxStreak);

        // The walk collects scheduled days newest first. The walk then reverses the list to walk oldest days first.
        var scheduledBackward = new List<DateOnly>();
        foreach (var d in DailySchedule.WalkScheduledDaysBackward(end, historyStart, effectiveRepeat, effectiveInterval, DailySchedule.MaxScheduledStepCap, effectiveWeekdays))
        {
            scheduledBackward.Add(d);
        }

        scheduledBackward.Reverse();

        var streakLengths = new List<int>();
        var currentLength = 0;
        foreach (var d in scheduledBackward)
        {
            var dayEvents = GetDayListOrNull(eventsByDay, d);
            if (IsCalendarDaySkipped(dayEvents))
            {
                continue;
            }

            if (IsCalendarDayNetCompleted(d, dayEvents, dailyLastCompletedOn))
            {
                currentLength++;
            }
            else if (currentLength > 0)
            {
                streakLengths.Add(currentLength);
                currentLength = 0;
            }
        }

        if (currentLength > 0)
        {
            streakLengths.Add(currentLength);
        }

        var longest = streakLengths.Count == 0 ? 0 : streakLengths.Max();

        // The previous streak is the finished streak right before the current streak.
        // When no streak is active, the previous streak is the most recent finished streak.
        // The walk ends at `end`. `end` is today when today is done and yesterday otherwise.
        // A trailing streak is active.
        int previous;
        if (streakLengths.Count == 0)
        {
            previous = 0;
        }
        else if (currentLength > 0)
        {
            previous = streakLengths.Count >= 2 ? Math.Min(MaxStreak, streakLengths[^2]) : 0;
        }
        else
        {
            previous = Math.Min(MaxStreak, streakLengths[^1]);
        }

        var current = ComputeStreak(dailyStart, repeat, repeatInterval, today, eventsByDay, dailyLastCompletedOn, weekdays);

        return new DailyStreakDetails(
            Math.Min(MaxStreak, current),
            Math.Min(MaxStreak, previous),
            Math.Min(MaxStreak, longest));
    }

    private static List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>? GetDayListOrNull(
        IReadOnlyDictionary<DateOnly, List<(DateTimeOffset OccurredAtUtc, ActivityEventType Type)>> eventsByDay,
        DateOnly day) =>
        eventsByDay.TryGetValue(day, out var list) ? list : null;
}
