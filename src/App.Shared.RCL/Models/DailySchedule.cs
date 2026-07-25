using App.Shared.RCL.Services;

namespace App.Shared.RCL.Models;

/// <summary>
///     Due dates for dailies from start date, repeat type, and interval. Uses local <see cref="DateOnly" /> (calendar)
///     based on the user's detected timezone, falling back to UTC if unavailable.
/// </summary>
public static class DailySchedule
{
    /// <summary>
    ///     Gets today's date in the user's local timezone, or UTC if no timezone service is available.
    ///     This is the primary "today" for daily scheduling.
    /// </summary>
    public static DateOnly LocalToday(IUserTimeZoneService? tz = null, TimeSpan? dayStartLocalTime = null)
    {
        return GetLocalDate(tz, DateTimeOffset.UtcNow, dayStartLocalTime);
    }

    /// <summary>
    ///     Gets yesterday's date in the user's local timezone, or UTC if no timezone service is available.
    /// </summary>
    public static DateOnly LocalYesterday(IUserTimeZoneService? tz = null, TimeSpan? dayStartLocalTime = null)
    {
        return LocalToday(tz, dayStartLocalTime).AddDays(-1);
    }

    private static DateOnly GetLocalDate(IUserTimeZoneService? tz, DateTimeOffset utcNow, TimeSpan? dayStartLocalTime)
    {
        DateTimeOffset local = tz is { IsDetected: true }
            ? tz.ConvertToLocal(utcNow)
            : utcNow;

        DateTime localDateTime = local.DateTime;
        if (dayStartLocalTime is { } start && start > TimeSpan.Zero && start < TimeSpan.FromDays(1) && localDateTime.TimeOfDay < start)
        {
            localDateTime = localDateTime.AddDays(-1);
        }

        return DateOnly.FromDateTime(localDateTime);
    }

    /// <summary>When no start is stored, treat the fallback date as the anchor so a new item is due immediately.</summary>
    public static DateOnly ResolveStartDateOrToday(DateOnly? dailyStart, DateOnly fallback)
    {
        return dailyStart ?? fallback;
    }

    /// <summary>
    ///     First calendar day used as the schedule anchor when walking streak history. When
    ///     <paramref name="dailyStart" /> is set, it is the real start. When it is <c>null</c>, the board treats
    ///     the daily as "due from today", but streak backfill and <see cref="Services.DailyStreakCalculator" />
    ///     must still count prior scheduled days — so we synthesize a start far enough before
    ///     <paramref name="notAfter" /> (aligned for weekly repeats). See
    ///     <see cref="App.Shared.RCL.Services.DailyStreakCalculator" />.
    /// </summary>
    /// <remarks>
    ///     All dates use the user's local timezone for scheduling. The board resets at local midnight.
    ///     If <paramref name="dailyStart" /> is after <paramref name="notAfter" /> (e.g. item created today but
    ///     streak backfill targets yesterday), we treat it like a missing start so history is not empty.
    ///     If <paramref name="dailyStart" /> <strong>equals</strong> <paramref name="notAfter" />, using it as
    ///     the floor would allow at most one backfill day; use a synthetic anchor so several prior days can
    ///     be scheduled the same as <see cref="Services.DailyStreakCalculator" /> and manual streak 3+.
    /// </remarks>
    public static DateOnly StreakHistoryScheduleStart(
        DateOnly? dailyStart,
        DateOnly notAfter,
        DailyRepeatType repeat,
        int rawInterval,
        int streakWindow)
    {
        if (dailyStart is { } d0 && d0 < notAfter)
        {
            return d0;
        }

        return StreakHistorySyntheticAnchor(notAfter, repeat, rawInterval, streakWindow);
    }

    private static DateOnly StreakHistorySyntheticAnchor(
        DateOnly notAfter,
        DailyRepeatType repeat,
        int rawInterval,
        int streakWindow)
    {
        int interval = Math.Max(1, Math.Min(999, rawInterval < 1 ? 1 : rawInterval));
        int s = Math.Min(9999, Math.Max(1, streakWindow));
        int padDays = repeat switch
        {
            DailyRepeatType.Daily => s * interval + 31,
            DailyRepeatType.Weekly => s * 7 * interval + 31,
            DailyRepeatType.Monthly => Math.Min(200_000, s * 31 * interval + 120),
            DailyRepeatType.Yearly => Math.Min(400_000, s * 366 * interval + 800),
            _ => s * interval + 31
        };

        DateOnly candidate = notAfter.AddDays(-padDays);
        if (repeat != DailyRepeatType.Weekly)
        {
            return candidate;
        }

        int dowDelta = ((int)notAfter.DayOfWeek - (int)candidate.DayOfWeek + 7) % 7;
        candidate = candidate.AddDays(dowDelta);
        if (candidate > notAfter)
        {
            candidate = candidate.AddDays(-7 * interval);
        }

        return candidate;
    }

    public static bool IsScheduledOn(
        DateOnly? dailyStart,
        DailyRepeatType repeat,
        int rawInterval,
        DateOnly on)
    {
        int interval = Math.Max(1, Math.Min(999, rawInterval < 1 ? 1 : rawInterval));
        DateOnly start = ResolveStartDateOrToday(dailyStart, on);
        if (on < start)
        {
            return false;
        }

        return repeat switch
        {
            DailyRepeatType.Daily => IsEveryNDaysFrom(start, on, interval),
            DailyRepeatType.Weekly => IsEveryNWeeksOnSameDowFrom(start, on, interval),
            DailyRepeatType.Monthly => IsEveryNMonthsSameDayFrom(start, on, interval),
            DailyRepeatType.Yearly => IsEveryNYearsSameDayFrom(start, on, interval),
            _ => IsEveryNDaysFrom(start, on, interval)
        };
    }

    public static bool IsScheduledOn(BoardItem daily, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(daily);
        return IsScheduledOn(daily.DailyStartDate, daily.DailyRepeat, daily.DailyRepeatInterval, on);
    }

    /// <summary>Whether this daily is checked off for the given calendar day (local timezone).</summary>
    public static bool IsCompleteForDate(BoardItem daily, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(daily);
        return daily.DailyLastCompletedOn == on;
    }

    /// <summary>Due = scheduled for <paramref name="on" /> and not yet completed for that day.</summary>
    public static bool IsDueOnDate(BoardItem daily, DateOnly on)
    {
        return IsScheduledOn(daily, on) && !IsCompleteForDate(daily, on);
    }

    public static int CountDuesOpen(IReadOnlyList<BoardItem> dailies, DateOnly on)
    {
        return dailies.Count(d => IsDueOnDate(d, on));
    }

    /// <summary>
    ///     Dailies that were due on the previous calendar day (local timezone) and not completed for that day, excluding
    ///     items already checked for <paramref name="today" /> to avoid clobbering a same-day check when backfilling.
    /// </summary>
    public static IReadOnlyList<BoardItem> GetYesterdayUncompletedDailies(
        IReadOnlyList<BoardItem> dailies,
        DateOnly today)
    {
        DateOnly yesterday = today.AddDays(-1);
        return [.. dailies
            .Where(d => d.DailyLastCompletedOn != today
                        && IsDueOnDate(d, yesterday))];
    }

    private static bool IsEveryNDaysFrom(DateOnly start, DateOnly on, int n)
    {
        int d = (on.ToDateTime(TimeOnly.MinValue) - start.ToDateTime(TimeOnly.MinValue)).Days;
        if (d < 0)
        {
            return false;
        }

        return d % n == 0;
    }

    private static bool IsEveryNWeeksOnSameDowFrom(DateOnly start, DateOnly on, int n)
    {
        int d = (on.ToDateTime(TimeOnly.MinValue) - start.ToDateTime(TimeOnly.MinValue)).Days;
        if (d < 0)
        {
            return false;
        }

        if (on.DayOfWeek != start.DayOfWeek)
        {
            return false;
        }

        if (d % 7 != 0)
        {
            return false;
        }

        int weekIndex = d / 7;
        return weekIndex % n == 0;
    }

    private static bool IsEveryNMonthsSameDayFrom(DateOnly start, DateOnly on, int n)
    {
        if (on < start)
        {
            return false;
        }

        int dueDay = Math.Min(start.Day, DateTime.DaysInMonth(on.Year, on.Month));
        if (on.Day != dueDay)
        {
            return false;
        }

        int m0 = start.Year * 12 + (start.Month - 1);
        int m1 = on.Year * 12 + (on.Month - 1);
        int monthDiff = m1 - m0;
        if (monthDiff < 0)
        {
            return false;
        }

        return monthDiff % n == 0;
    }

    private static bool IsEveryNYearsSameDayFrom(DateOnly start, DateOnly on, int n)
    {
        if (on < start)
        {
            return false;
        }

        int dueDay = Math.Min(start.Day, DateTime.DaysInMonth(on.Year, on.Month));
        if (on.Month != start.Month || on.Day != dueDay)
        {
            return false;
        }

        int yDiff = on.Year - start.Year;
        return yDiff >= 0 && yDiff % n == 0;
    }
}
