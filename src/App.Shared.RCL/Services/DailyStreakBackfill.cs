using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>
///     Resolves the calendar days implied by a daily streak counter for activity statistics,
///     and marks synthetic <see cref="ActivityEventType.DailyComplete" /> rows inserted for that backfill.
/// </summary>
public static class DailyStreakBackfill
{
    /// <summary>UTC noon — real toggles use <see cref="DateTimeOffset.UtcNow" />, so we can tell synthetics apart.</summary>
    public const int StreakBackfillHourUtc = 12;

    /// <summary>
    ///     The most recent <paramref name="streakCount" /> scheduled days on or before <paramref name="notAfter" />,
    ///     in iteration order (newest first). Fewer than <paramref name="streakCount" /> if the schedule
    ///     or start date does not allow more days. For manual streak from the daily edit dialog, use
    ///     <c>notAfter = <see cref="DailySchedule.UtcToday" />.AddDays(-1)</c> so the count is previous days only
    ///     and does not mark today.
    /// </summary>
    public static IReadOnlyList<DateOnly> GetLastNScheduledCompletionDays(
        DateOnly? dailyStart,
        DailyRepeatType repeat,
        int repeatInterval,
        int streakCount,
        DateOnly notAfter)
    {
        if (streakCount <= 0)
        {
            return [];
        }

        var n = Math.Min(9999, streakCount);
        var scheduleStart = DailySchedule.StreakHistoryScheduleStart(
            dailyStart, notAfter, repeat, repeatInterval, n);
        var list = new List<DateOnly>(n);
        var d = notAfter;
        var maxSteps = 20_000;
        while (list.Count < n && maxSteps-- > 0)
        {
            if (d < scheduleStart)
            {
                break;
            }

            if (DailySchedule.IsScheduledOn(scheduleStart, repeat, repeatInterval, d))
            {
                list.Add(d);
            }

            d = d.AddDays(-1);
        }

        return list;
    }

    public static bool IsStreakBackfillTimestamp(DateTimeOffset occurredAtUtc) =>
        occurredAtUtc.Hour == StreakBackfillHourUtc
        && occurredAtUtc.Minute == 0
        && occurredAtUtc.Second == 0
        && occurredAtUtc.Millisecond == 0;

    public static DateTimeOffset StreakBackfillOccurredAt(DateOnly day) =>
        new(day.Year, day.Month, day.Day, StreakBackfillHourUtc, 0, 0, TimeSpan.Zero);
}
