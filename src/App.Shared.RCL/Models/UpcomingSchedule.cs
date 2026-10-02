namespace App.Shared.RCL.Models;

/// <summary>One calendar day in the upcoming view.</summary>
public sealed record UpcomingDay(
    DateOnly Date,
    bool IsToday,
    IReadOnlyList<BoardItem> Dailies,
    IReadOnlyList<BoardItem> TodosDue);

/// <summary>
/// Projects dailies and to-dos into calendar days using <see cref="DailySchedule" />.
/// Today shows only items still due, future days show everything scheduled.
/// </summary>
public static class UpcomingSchedule
{
    /// <summary>How many days the upcoming view covers, including today.</summary>
    public const int DefaultDaysAhead = 7;

    /// <summary>Hard cap on the projection window so a wide interval cannot spin.</summary>
    public const int MaxDaysAhead = 31;

    public static IReadOnlyList<UpcomingDay> GetUpcomingDays(
        BoardSnapshot snapshot,
        DateOnly today,
        int daysAhead = DefaultDaysAhead)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var count = Math.Max(1, Math.Min(MaxDaysAhead, daysAhead));
        List<UpcomingDay> days = [];
        for (var offset = 0; offset < count; offset++)
        {
            var date = today.AddDays(offset);
            var isToday = offset == 0;
            List<BoardItem> dailies = [.. snapshot.Dailies
                .Where(d => isToday ? DailySchedule.IsDueOnDate(d, date) : DailySchedule.IsScheduledOn(d, date))
                .OrderBy(d => d.Title, StringComparer.Ordinal)];
            List<BoardItem> todos = [.. snapshot.Todos
                .Where(t => !t.IsCompleted && t.TodoDueDate == date)
                .OrderBy(t => t.Title, StringComparer.Ordinal)];
            days.Add(new UpcomingDay(date, isToday, dailies, todos));
        }

        return days;
    }

    /// <summary>To-dos past their due date, oldest first.</summary>
    public static IReadOnlyList<BoardItem> GetOverdueTodos(BoardSnapshot snapshot, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return [.. snapshot.Todos
            .Where(t => !t.IsCompleted && t.TodoDueDate is { } due && due < today)
            .OrderBy(t => t.TodoDueDate)
            .ThenBy(t => t.Title, StringComparer.Ordinal)];
    }

    /// <summary>Short schedule reason shown under an upcoming daily, e.g. "Mon, Wed, Fri" or "Every 2 weeks".</summary>
    public static string DescribeDaily(BoardItem daily)
    {
        ArgumentNullException.ThrowIfNull(daily);
        var interval = Math.Max(1, Math.Min(999, daily.DailyRepeatInterval));
        var mask = DailyWeekdays.Normalize(daily.DailyWeekdays);
        return daily.DailyRepeat switch
        {
            DailyRepeatType.Daily => interval == 1 ? "Daily" : $"Every {interval} days",
            DailyRepeatType.Weekly when mask != DailyWeekdays.None => DescribeWeekdays(mask, interval),
            DailyRepeatType.Weekly => interval == 1 ? "Weekly" : $"Every {interval} weeks",
            DailyRepeatType.Monthly => interval == 1 ? "Monthly" : $"Every {interval} months",
            DailyRepeatType.Yearly => interval == 1 ? "Yearly" : $"Every {interval} years",
            _ => interval == 1 ? "Daily" : $"Every {interval} days",
        };
    }

    private static string DescribeWeekdays(int mask, int interval)
    {
        var names = DailyWeekdays.ToDays(mask).Select(ShortName);
        var days = string.Join(", ", names);
        return interval == 1 ? days : $"{days} · every {interval} weeks";
    }

    private static string ShortName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Mon",
        DayOfWeek.Tuesday => "Tue",
        DayOfWeek.Wednesday => "Wed",
        DayOfWeek.Thursday => "Thu",
        DayOfWeek.Friday => "Fri",
        DayOfWeek.Saturday => "Sat",
        DayOfWeek.Sunday => "Sun",
        _ => day.ToString(),
    };
}
