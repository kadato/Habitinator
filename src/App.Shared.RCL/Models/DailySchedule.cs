namespace App.Shared.RCL.Models;

/// <summary>
/// due dates for dailies from start date, repeat type, and interval. Uses UTC <see cref="DateOnly"/> (calendar) throughout.
/// </summary>
public static class DailySchedule
{
    public static DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>When no start is stored, treat "today" (UTC) as the anchor so a new item is due immediately.</summary>
    public static DateOnly ResolveStartDateOrToday(DateOnly? dailyStart) => dailyStart ?? UtcToday;

    public static bool IsScheduledOn(
        DateOnly? dailyStart,
        DailyRepeatType repeat,
        int rawInterval,
        DateOnly on)
    {
        int interval = Math.Max(1, Math.Min(999, rawInterval < 1 ? 1 : rawInterval));
        DateOnly start = ResolveStartDateOrToday(dailyStart);
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

    public static bool IsScheduledOn(BoardItem daily, DateOnly on) =>
        IsScheduledOn(daily.DailyStartDate, daily.DailyRepeat, daily.DailyRepeatInterval, on);

    /// <summary>Whether this daily is checked off for the given calendar day (UTC).</summary>
    public static bool IsCompleteForDate(BoardItem daily, DateOnly on) =>
        daily.DailyLastCompletedOn == on;

    /// <summary>Due = scheduled for <paramref name="on"/> and not yet completed for that day.</summary>
    public static bool IsDueOnDate(BoardItem daily, DateOnly on) =>
        IsScheduledOn(daily, on) && !IsCompleteForDate(daily, on);

    public static int CountDuesOpen(IReadOnlyList<BoardItem> dailies, DateOnly on) =>
        dailies.Count(d => IsDueOnDate(d, on));

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
