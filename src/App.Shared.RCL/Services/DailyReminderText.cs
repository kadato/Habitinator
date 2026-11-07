using System.Globalization;
using System.Text;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>
///     Builds the title and body for the daily board reminder. Uses the same "today" calendar as
///     the board, see <see cref="DailySchedule" /> UTC, so dailies and to-dos match what the user sees in the app.
/// </summary>
public static class DailyReminderText
{
    public const string DefaultTitle = "Habitinator";

    /// <param name="todayUtc">Calendar day to evaluate, e.g. <see cref="DailySchedule.LocalToday" />.</param>
    public static (string Title, string Body) Build(BoardSnapshot snapshot, DateOnly todayUtc, int maxBodyLength = 1800)
    {
        return Build(snapshot, todayUtc, null, maxBodyLength);
    }

    /// <param name="todayUtc">Calendar day to evaluate, e.g. <see cref="DailySchedule.LocalToday" />.</param>
    public static (string Title, string Body) Build(
        BoardSnapshot snapshot,
        DateOnly todayUtc,
        string? dateFormat,
        int maxBodyLength = 1800)
    {
        var tomorrow = todayUtc.AddDays(1);
        List<string> dailies = [.. snapshot.Dailies
            .Where(d => DailySchedule.IsDueOnDate(d, todayUtc))
            .Select(d => d.Title)
            .OrderBy(t => t, StringComparer.Ordinal)];

        List<BoardItem> deadlineTodos = [.. snapshot.Todos
            .Where(t => !t.IsCompleted && t.TodoDueDate is { } due && due <= todayUtc)
            .OrderBy(t => t.TodoDueDate)
            .ThenBy(t => t.Title, StringComparer.Ordinal)];

        List<string> tomorrowDailies = [.. snapshot.Dailies
            .Where(d => DailySchedule.IsScheduledOn(d, tomorrow))
            .Select(d => d.Title)
            .OrderBy(t => t, StringComparer.Ordinal)];
        List<BoardItem> tomorrowTodos = [.. snapshot.Todos
            .Where(t => !t.IsCompleted && t.TodoDueDate == tomorrow)
            .OrderBy(t => t.Title, StringComparer.Ordinal)];

        if (dailies.Count == 0 && deadlineTodos.Count == 0 && tomorrowDailies.Count == 0 && tomorrowTodos.Count == 0)
        {
            return (DefaultTitle,
                "You have no dailies due and no to-dos with a due date for today. Open the app to work on your habits and tasks.");
        }

        StringBuilder sb = new(256);
        if (dailies.Count > 0)
        {
            sb.Append("Dailies due: ");
            sb.Append(string.Join(", ", dailies));
        }

        if (deadlineTodos.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            AppendDeadlineTodos(sb, deadlineTodos, todayUtc, dateFormat);
        }

        var tomorrowTotal = tomorrowDailies.Count + tomorrowTodos.Count;
        if (tomorrowTotal > 0)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.Append(CultureInfo.InvariantCulture, $"Tomorrow: {tomorrowTotal} scheduled");
            if (tomorrowDailies.Count > 0 && tomorrowTodos.Count > 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $", {tomorrowDailies.Count} dailies and {tomorrowTodos.Count} to-dos");
            }
            else if (tomorrowDailies.Count > 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $", {tomorrowDailies.Count} dailies");
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture, $", {tomorrowTodos.Count} to-dos");
            }
            sb.Append(". Open Upcoming to plan.");
        }

        var body = sb.ToString();
        if (maxBodyLength > 0 && body.Length > maxBodyLength)
        {
            body = body[..(maxBodyLength - 1)] + "…";
        }

        return (DefaultTitle, body);
    }

    private static void AppendDeadlineTodos(
        StringBuilder sb,
        List<BoardItem> deadlineTodos,
        DateOnly todayUtc,
        string? dateFormat)
    {
        sb.Append("To-dos (deadline): ");
        for (var i = 0; i < deadlineTodos.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            var t = deadlineTodos[i];
            var due = t.TodoDueDate ?? todayUtc;
            var suffix = due < todayUtc
                ? " (overdue)"
                : string.Empty;
            sb.Append(t.Title);
            if (due != todayUtc)
            {
                sb.Append(" - ");
                sb.Append(FormatDate(due, dateFormat));
            }

            sb.Append(suffix);
        }
    }

    private static string FormatDate(DateOnly date, string? dateFormat)
    {
        var format = string.IsNullOrWhiteSpace(dateFormat) ? UserPreferences.DefaultDateFormat : dateFormat;
        return date.ToString(format, CultureInfo.InvariantCulture);
    }
}
