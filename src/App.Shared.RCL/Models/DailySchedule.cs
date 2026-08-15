using App.Shared.RCL.Services;

namespace App.Shared.RCL.Models;

/// <summary>
///     Due dates for dailies from start date, repeat type, and interval. Uses local <see cref="DateOnly" />, the calendar
///     based on the user's detected timezone, falling back to UTC if unavailable.
/// </summary>
public static class DailySchedule
{
    /// <summary>Hard cap on how many days a backward schedule walk may step before giving up.</summary>
    public const int MaxScheduledStepCap = 20_000;

    /// <summary>Hard cap on how many history days any streak walk or anchor may span.</summary>
    public const int MaxHistoryDays = 9999;

    /// <summary>
    ///     Gets today's date in the user's local timezone, or UTC if no timezone service is available.
    ///     This is the primary "today" for daily scheduling.
    /// </summary>
    public static DateOnly LocalToday(IUserTimeZoneService? tz = null, TimeSpan? dayStartLocalTime = null)
    {
        return LocalToday(SystemClock.Instance, tz, dayStartLocalTime);
    }

    /// <summary>
    ///     Gets today's date in the user's local timezone from the given clock, or UTC if no
    ///     timezone service is available. This is the primary "today" for daily scheduling.
    /// </summary>
    public static DateOnly LocalToday(IClock clock, IUserTimeZoneService? tz = null, TimeSpan? dayStartLocalTime = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return LocalDay(clock.UtcNow, tz, dayStartLocalTime);
    }

    /// <summary>
    ///     The calendar day an instant falls on in the user's local timezone, applying the day-start
    ///     rollback. With no timezone service the instant is treated as UTC, matching <see cref="LocalToday" />.
    ///     Used to assign activity events to the same calendar days the schedule walks.
    /// </summary>
    public static DateOnly LocalDay(DateTimeOffset utcInstant, IUserTimeZoneService? tz = null, TimeSpan? dayStartLocalTime = null)
    {
        var local = tz is { IsDetected: true }
            ? tz.ConvertToLocal(utcInstant)
            : utcInstant;

        var localDateTime = local.DateTime;
        if (dayStartLocalTime is { } start && start > TimeSpan.Zero && start < TimeSpan.FromDays(1) && localDateTime.TimeOfDay < start)
        {
            localDateTime = localDateTime.AddDays(-1);
        }

        return DateOnly.FromDateTime(localDateTime);
    }

    /// <summary>When no start is stored, treat the fallback date as the anchor so a new item is due immediately.</summary>
    private static DateOnly ResolveStartDateOrToday(DateOnly? dailyStart, DateOnly fallback)
    {
        return dailyStart ?? fallback;
    }

    /// <summary>
    ///     First calendar day used as the schedule anchor when walking streak history. When
    ///     <paramref name="dailyStart" /> is set and before <paramref name="notAfter" />, it is the real
    ///     start. When it is <c>null</c>, the board treats the daily as "due from today" every day is
    ///     scheduled, but streak backfill and <see cref="Services.DailyStreakCalculator" /> must still
    ///     count prior scheduled days, so we create the start far enough before
    ///     <paramref name="notAfter" />. When <paramref name="dailyStart" /> is on or after
    ///     <paramref name="notAfter" />, the case of an item created today but streak history targeting yesterday,
    ///     we extend the real schedule backward with the same phase instead of treating the history as
    ///     empty, so backfilled days line up with the days the board actually schedules.
    /// </summary>
    /// <remarks>
    ///     All dates use the user's local timezone for scheduling. The board resets at local midnight.
    ///     A phase mismatch here shows as streaks stuck at 0 or counting the wrong days for interval
    ///     dailies, so the created anchor must keep the schedule pattern of the real start.
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

        return dailyStart is { } real
            ? StreakHistoryPhaseMatchedAnchor(real, notAfter, repeat, rawInterval, streakWindow)
            : StreakHistoryEveryDayAnchor(notAfter, streakWindow);
    }

    private static DateOnly StreakHistoryEveryDayAnchor(DateOnly notAfter, int streakWindow)
    {
        var s = Math.Min(MaxHistoryDays, Math.Max(1, streakWindow));
        return notAfter.AddDays(-(s + 31));
    }

    /// <summary>
    ///     The latest date at or before <paramref name="notAfter" /> from which the schedule pattern matches
    ///     <paramref name="dailyStart" />'s phase and that is at least <paramref name="streakWindow" />
    ///     periods behind the walk end, so streak walks and backfill never run out of history.
    /// </summary>
    private static DateOnly StreakHistoryPhaseMatchedAnchor(
        DateOnly dailyStart,
        DateOnly notAfter,
        DailyRepeatType repeat,
        int rawInterval,
        int streakWindow)
    {
        var interval = Math.Max(1, Math.Min(999, rawInterval < 1 ? 1 : rawInterval));
        var s = Math.Min(MaxHistoryDays, Math.Max(1, streakWindow));
        return repeat switch
        {
            DailyRepeatType.Weekly => PhaseMatchedPeriodAnchor(dailyStart, notAfter, s, 7 * interval, 31),
            DailyRepeatType.Monthly => PhaseMatchedMonthAnchor(dailyStart, notAfter, interval, s),
            DailyRepeatType.Yearly => PhaseMatchedYearAnchor(dailyStart, notAfter, interval, s),
            _ => PhaseMatchedPeriodAnchor(dailyStart, notAfter, s, interval, 31)
        };
    }

    private static DateOnly PhaseMatchedPeriodAnchor(
        DateOnly dailyStart,
        DateOnly notAfter,
        int window,
        int periodDays,
        int slackDays)
    {
        var diff = (dailyStart.ToDateTime(TimeOnly.MinValue) - notAfter.ToDateTime(TimeOnly.MinValue)).Days;
        var pad = window * periodDays + slackDays;
        var k = (int)Math.Ceiling((diff + pad) / (double)periodDays);
        var maxK = (dailyStart.ToDateTime(TimeOnly.MinValue) - DateTime.MinValue).Days / periodDays;
        if (k > maxK)
        {
            return DateOnly.MinValue;
        }

        return dailyStart.AddDays(-k * periodDays);
    }

    private static DateOnly PhaseMatchedMonthAnchor(
        DateOnly dailyStart,
        DateOnly notAfter,
        int interval,
        int window)
    {
        var m = MonthIndex(dailyStart) - MonthIndex(notAfter);
        var padMonths = window * interval + 4;
        var k = (int)Math.Ceiling((m + padMonths) / (double)interval);
        var maxK = MonthIndex(dailyStart) / interval;
        if (k > maxK)
        {
            return DateOnly.MinValue;
        }

        for (var bump = 0; bump <= interval; bump++)
        {
            var candidate = AddMonths(dailyStart, -(k + bump) * interval);
            var day = Math.Min(dailyStart.Day, DateTime.DaysInMonth(candidate.Year, candidate.Month));
            if (day == dailyStart.Day)
            {
                return candidate;
            }
        }

        return AddMonths(dailyStart, -k * interval);
    }

    private static DateOnly PhaseMatchedYearAnchor(
        DateOnly dailyStart,
        DateOnly notAfter,
        int interval,
        int window)
    {
        var yDiff = dailyStart.Year - notAfter.Year;
        var padYears = window * interval + 2;
        var k = (int)Math.Ceiling((yDiff + padYears) / (double)interval);
        var maxK = (dailyStart.Year - 1) / interval;
        if (k > maxK)
        {
            return DateOnly.MinValue;
        }

        for (var bump = 0; bump <= 4 * interval; bump++)
        {
            var year = dailyStart.Year - (k + bump) * interval;
            var day = Math.Min(dailyStart.Day, DateTime.DaysInMonth(year, dailyStart.Month));
            if (day == dailyStart.Day)
            {
                return new DateOnly(year, dailyStart.Month, day);
            }
        }

        var fallbackYear = dailyStart.Year - k * interval;
        return new DateOnly(
            fallbackYear,
            dailyStart.Month,
            Math.Min(dailyStart.Day, DateTime.DaysInMonth(fallbackYear, dailyStart.Month)));
    }

    private static int MonthIndex(DateOnly d) => d.Year * 12 + d.Month - 1;

    private static DateOnly AddMonths(DateOnly d, int months)
    {
        var result = d.ToDateTime(TimeOnly.MinValue).AddMonths(months);
        return DateOnly.FromDateTime(result);
    }

    public static bool IsScheduledOn(
        DateOnly? dailyStart,
        DailyRepeatType repeat,
        int rawInterval,
        DateOnly on)
    {
        var interval = Math.Max(1, Math.Min(999, rawInterval < 1 ? 1 : rawInterval));
        var start = ResolveStartDateOrToday(dailyStart, on);
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

    /// <summary>
    ///     Yields scheduled days walking backward from <paramref name="from" />, newest first, stopping
    ///     before <paramref name="scheduleAnchor" /> and after <paramref name="maxSteps" /> days.
    /// </summary>
    public static IEnumerable<DateOnly> WalkScheduledDaysBackward(
        DateOnly from,
        DateOnly scheduleAnchor,
        DailyRepeatType repeat,
        int interval,
        int maxSteps = MaxScheduledStepCap)
    {
        var d = from;
        var steps = maxSteps;
        while (steps-- > 0)
        {
            if (d < scheduleAnchor)
            {
                yield break;
            }

            if (IsScheduledOn(scheduleAnchor, repeat, interval, d))
            {
                yield return d;
            }

            d = d.AddDays(-1);
        }
    }

    /// <summary>Whether this daily is checked off for the given calendar day in the local timezone.</summary>
    public static bool IsCompleteForDate(BoardItem daily, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(daily);
        return daily.DailyLastCompletedOn == on;
    }

    /// <summary>
    ///     Whether the daily counts as checked for <paramref name="today" />: either completed today
    ///     explicitly, or the legacy state of being completed with no recorded date.
    /// </summary>
    public static bool IsCompletedForToday(DateOnly? dailyLastCompletedOn, bool isCompleted, DateOnly today) =>
        dailyLastCompletedOn == today || (dailyLastCompletedOn is null && isCompleted);

    /// <summary>
    ///     The single daily check/uncheck rule shared by the web persistence layer, the MAUI local
    ///     store, and the optimistic UI: checking sets today, unchecking clears the date.
    /// </summary>
    public static (DateOnly? DailyLastCompletedOn, bool IsCompleted) ToggleForToday(
        DateOnly? dailyLastCompletedOn,
        bool isCompleted,
        DateOnly today)
    {
        return IsCompletedForToday(dailyLastCompletedOn, isCompleted, today)
            ? (null, false)
            : (today, true);
    }

    /// <summary>
    ///     Whether a retro check-in for <paramref name="completedOn" /> is accepted, mirroring the
    ///     server's <c>complete-for-date</c> guards: a past date that is scheduled for the item and
    ///     not already completed for that day, and the item not already checked for today.
    /// </summary>
    public static bool CanCompleteForDate(
        DateOnly? dailyStart,
        DailyRepeatType repeat,
        int interval,
        DateOnly? dailyLastCompletedOn,
        DateOnly completedOn,
        DateOnly today) =>
        completedOn < today
        && dailyLastCompletedOn != today
        && dailyLastCompletedOn != completedOn
        && IsScheduledOn(dailyStart, repeat, interval, completedOn);

    /// <summary>Due = scheduled for <paramref name="on" /> and not yet completed for that day.</summary>
    public static bool IsDueOnDate(BoardItem daily, DateOnly on)
    {
        return IsScheduledOn(daily, on) && !IsCompleteForDate(daily, on);
    }

    /// <summary>
    ///     Dailies that were due on the previous calendar day in the local timezone and not completed for that day, excluding
    ///     items already checked for <paramref name="today" /> to avoid clobbering a same-day check when backfilling.
    ///     Null-start dailies are "due from today" on the board, so one created on the local
    ///     <paramref name="today" /> did not exist yesterday and is not offered in the catch-up dialog.
    /// </summary>
    public static IReadOnlyList<BoardItem> GetYesterdayUncompletedDailies(
        IReadOnlyList<BoardItem> dailies,
        DateOnly today,
        IUserTimeZoneService? tz = null)
    {
        var yesterday = today.AddDays(-1);
        return [.. dailies
            .Where(d => d.DailyLastCompletedOn != today
                        && !IsNewNullStartDaily(d, today, tz)
                        && IsDueOnDate(d, yesterday))];
    }

    private static bool IsNewNullStartDaily(BoardItem item, DateOnly today, IUserTimeZoneService? tz)
    {
        if (item.DailyStartDate is not null || item.CreatedAtUtc is not { } createdUtc)
        {
            return false;
        }

        var local = tz is { IsDetected: true } ? tz.ConvertToLocal(createdUtc) : createdUtc;
        return DateOnly.FromDateTime(local.DateTime) >= today;
    }

    private static bool IsEveryNDaysFrom(DateOnly start, DateOnly on, int n)
    {
        var d = (on.ToDateTime(TimeOnly.MinValue) - start.ToDateTime(TimeOnly.MinValue)).Days;
        if (d < 0)
        {
            return false;
        }

        return d % n == 0;
    }

    private static bool IsEveryNWeeksOnSameDowFrom(DateOnly start, DateOnly on, int n)
    {
        var d = (on.ToDateTime(TimeOnly.MinValue) - start.ToDateTime(TimeOnly.MinValue)).Days;
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

        var weekIndex = d / 7;
        return weekIndex % n == 0;
    }

    private static bool IsEveryNMonthsSameDayFrom(DateOnly start, DateOnly on, int n)
    {
        if (on < start)
        {
            return false;
        }

        var dueDay = Math.Min(start.Day, DateTime.DaysInMonth(on.Year, on.Month));
        if (on.Day != dueDay)
        {
            return false;
        }

        var m0 = start.Year * 12 + (start.Month - 1);
        var m1 = on.Year * 12 + (on.Month - 1);
        var monthDiff = m1 - m0;
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

        var dueDay = Math.Min(start.Day, DateTime.DaysInMonth(on.Year, on.Month));
        if (on.Month != start.Month || on.Day != dueDay)
        {
            return false;
        }

        var yDiff = on.Year - start.Year;
        return yDiff >= 0 && yDiff % n == 0;
    }
}
