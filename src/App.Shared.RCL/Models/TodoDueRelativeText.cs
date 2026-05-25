namespace App.Shared.RCL.Models;

/// <summary>
/// Human-readable relative labels for to-do due dates.
/// </summary>
public static class TodoDueRelativeText
{
    public static string Format(DateOnly dueDate, DateOnly today)
    {
        var days = dueDate.DayNumber - today.DayNumber;

        if (days < 0)
            return FormatOverdue(-days);

        if (days == 0)
            return "Due today";

        if (days == 1)
            return "Due tomorrow";

        return FormatFuture(days);
    }

    private static string FormatOverdue(int days) =>
        days == 1 ? "1 day overdue" : $"{days} days overdue";

    private static string FormatFuture(int days)
    {
        if (days <= 30)
            return $"{days} days left";

        var months = days / 30;
        return months == 1 ? "1 month left" : $"{months} months left";
    }
}
