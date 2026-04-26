namespace App.Shared.RCL.Models;

/// <summary>
///     due dates for dailies from start date, repeat type, and interval. Uses UTC <see cref="DateOnly" /> (calendar)
///     throughout.
/// </summary>
public static class DailySchedule
{
    public static DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>When no start is stored, treat "today" (UTC) as the anchor so a new item is due immediately.</summary>
    public static DateOnly ResolveStartDateOrToday(DateOnly? dailyStart)
    {
        return dailyStart ?? UtcToday;
    }

    public static bool IsScheduledOn(
        DateOnly? dailyStart,
        DailyRepeatType repeat,
        int rawInterval,
        DateOnly on)
    {
        var interval = Math.Max(1, Math.Min(999, rawInterval < 1 ? 1 : rawInterval));
        var start = ResolveStartDateOrToday(dailyStart);
        if (on < start) return false;

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
        return IsScheduledOn(daily.DailyStartDate, daily.DailyRepeat, daily.DailyRepeatInterval, on);
    }

    /// <summary>Whether this daily is checked off for the given calendar day (UTC).</summary>
    public static bool IsCompleteForDate(BoardItem daily, DateOnly on)
    {
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
    ///     Dailies that were due on the previous calendar day (UTC) and not completed for that day, excluding
    ///     items already checked for <paramref name="today" /> to avoid clobbering a same-day check when backfilling.
    /// </summary>
    public static IReadOnlyList<BoardItem> GetYesterdayUncompletedDailies(
        IReadOnlyList<BoardItem> dailies,
        DateOnly today)
    {
        var yesterday = today.AddDays(-1);
        return dailies
            .Where(d => d.DailyLastCompletedOn != today
                        && IsDueOnDate(d, yesterday))
            .ToList();
    }

    private static bool IsEveryNDaysFrom(DateOnly start, DateOnly on, int n)
    {
        var d = (on.ToDateTime(TimeOnly.MinValue) - start.ToDateTime(TimeOnly.MinValue)).Days;
        if (d < 0) return false;

        return d % n == 0;
    }

    private static bool IsEveryNWeeksOnSameDowFrom(DateOnly start, DateOnly on, int n)
    {
        var d = (on.ToDateTime(TimeOnly.MinValue) - start.ToDateTime(TimeOnly.MinValue)).Days;
        if (d < 0) return false;

        if (on.DayOfWeek != start.DayOfWeek) return false;

        if (d % 7 != 0) return false;

        var weekIndex = d / 7;
        return weekIndex % n == 0;
    }

    private static bool IsEveryNMonthsSameDayFrom(DateOnly start, DateOnly on, int n)
    {
        if (on < start) return false;

        var dueDay = Math.Min(start.Day, DateTime.DaysInMonth(on.Year, on.Month));
        if (on.Day != dueDay) return false;

        var m0 = start.Year * 12 + (start.Month - 1);
        var m1 = on.Year * 12 + (on.Month - 1);
        var monthDiff = m1 - m0;
        if (monthDiff < 0) return false;

        return monthDiff % n == 0;
    }

    private static bool IsEveryNYearsSameDayFrom(DateOnly start, DateOnly on, int n)
    {
        if (on < start) return false;

        var dueDay = Math.Min(start.Day, DateTime.DaysInMonth(on.Year, on.Month));
        if (on.Month != start.Month || on.Day != dueDay) return false;

        var yDiff = on.Year - start.Year;
        return yDiff >= 0 && yDiff % n == 0;
    }
}
