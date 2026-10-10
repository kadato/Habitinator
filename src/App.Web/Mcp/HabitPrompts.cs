using System.ComponentModel;
using System.Globalization;

using App.Web.Services;

using Microsoft.Extensions.AI;

using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerPromptType]
public sealed class HabitPrompts(
    BoardPersistenceService boards,
    ActivityStatisticsService stats,
    IHttpContextAccessor http)
{
    [McpServerPrompt, Description("Plans today from dailies, todos, and today detail. Asks for a short ordered plan.")]
    public async Task<IEnumerable<ChatMessage>> PlanDay(
        [Description("Date as yyyy-MM-dd. Null means today.")] string? date = null,
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var snapshot = await boards.GetSnapshotAsync(userId, 200, cancellationToken);
        DateOnly? day = date is not null
            && DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
        var detail = day.HasValue
            ? await stats.GetActivityDayDetailAsync(userId, day.Value, tag, cancellationToken)
            : null;

        var dailies = string.Join("\n", snapshot.Dailies.Take(30).Select(d => $"- {d.Title} (streak {d.Counter})"));
        var todos = string.Join("\n", snapshot.Todos.Take(30).Select(t => $"- {t.Title}"));
        return [
            new(ChatRole.User,
                $"Plan my day{(day.HasValue ? $" for {day.Value:yyyy-MM-dd}" : "")}{(tag is null ? "" : $" tagged {tag}")}.\n" +
                $"Dailies:\n{dailies}\nTodos:\n{todos}\n" +
                (detail is null ? "" : $"Today so far: {detail}.") +
                "\nReply with the top 5 items in order and one line for each that says why.")
        ];
    }

    [McpServerPrompt, Description("Reviews the last period: dashboard with habit and daily contributions. Asks for wins, gaps, and next week focus.")]
    public async Task<IEnumerable<ChatMessage>> ReviewWeek(
        [Description("Period key such as last_7_days or last_30_days.")] string? period = "last_7_days",
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var overview = await stats.GetOverviewAsync(userId, period, tag, cancellationToken);
        return [
            new(ChatRole.User,
                $"Review my period {period ?? "default"}{(tag is null ? "" : $" tagged {tag}")}.\n" +
                $"Overview: {overview}.\n" +
                "Reply with wins, gaps, and three focus items for next week.")
        ];
    }

    [McpServerPrompt, Description("Coaches one habit: its counters and schedule. Asks for one small next step.")]
    public async Task<IEnumerable<ChatMessage>> HabitCoach(
        [Description("Habit item id.")] string habitId,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (!Guid.TryParse(habitId, out var id))
        {
            return [new(ChatRole.User, $"Coach habit {habitId}. The id is not a valid GUID. Ask me for the right id.")];
        }

        var item = await boards.GetItemAsync(userId, id, cancellationToken);
        if (item is null)
        {
            return [new(ChatRole.User, $"Coach habit {habitId}. It was not found. Ask me which habit I mean.")];
        }

        return [
            new(ChatRole.User,
                $"Coach my habit '{item.Title}': plus {item.Counter}, minus {item.NegativeCounter}, reset {item.ResetPeriod}.\n" +
                "Reply with one small next step I can do today.")
        ];
    }
}
