using System.Globalization;
using System.Text;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>
///     Builds the title and body for the daily board reminder. Uses the same “today” calendar as
///     the board (<see cref="DailySchedule" /> UTC) so dailies and to-dos match what the user sees in the app.
/// </summary>
public static class DailyReminderText
{
    public const string DefaultTitle = "Habitinator";

    /// <param name="todayUtc">Calendar day in UTC, e.g. <see cref="DailySchedule.UtcToday" />.</param>
    public static (string Title, string Body) Build(BoardSnapshot snapshot, DateOnly todayUtc, int maxBodyLength = 1800)
    {
        return Build(snapshot, todayUtc, null, maxBodyLength);
    }

    /// <param name="todayUtc">Calendar day in UTC, e.g. <see cref="DailySchedule.UtcToday" />.</param>
    public static (string Title, string Body) Build(
        BoardSnapshot snapshot,
        DateOnly todayUtc,
        string? dateFormat,
        int maxBodyLength = 1800)
    {
        var dailies = snapshot.Dailies
            .Where(d => DailySchedule.IsDueOnDate(d, todayUtc))
            .Select(d => d.Title)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        var deadlineTodos = snapshot.Todos
            .Where(t => !t.IsCompleted && t.TodoDueDate is { } due && due <= todayUtc)
            .OrderBy(t => t.TodoDueDate)
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .ToList();

        if (dailies.Count == 0 && deadlineTodos.Count == 0)
            return (DefaultTitle,
                "You have no dailies due and no to-dos with a due date for today. Open the app to work on your habits and tasks.");

        var sb = new StringBuilder(256);
        if (dailies.Count > 0)
        {
            sb.Append("Dailies due: ");
            sb.Append(string.Join(", ", dailies));
        }

        if (deadlineTodos.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();

            sb.Append("To-dos (deadline): ");
            for (var i = 0; i < deadlineTodos.Count; i++)
            {
                if (i > 0) sb.Append(", ");

                var t = deadlineTodos[i];
                var due = t.TodoDueDate!.Value;
                var suffix = due < todayUtc
                    ? " (overdue)"
                    : string.Empty;
                sb.Append(t.Title);
                if (due != todayUtc)
                {
                    sb.Append(" — ");
                    sb.Append(FormatDate(due, dateFormat));
                }

                sb.Append(suffix);
            }
        }

        var body = sb.ToString();
        if (body.Length > maxBodyLength) body = body[..(maxBodyLength - 1)] + "…";

        return (DefaultTitle, body);
    }

    private static string FormatDate(DateOnly date, string? dateFormat)
    {
        var format = string.IsNullOrWhiteSpace(dateFormat) ? "yyyy/MM/dd" : dateFormat;
        return date.ToString(format, CultureInfo.InvariantCulture);
    }
}
