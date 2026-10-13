using System.ComponentModel;
using System.Text.Json;

using App.Web.Services;

using Microsoft.Extensions.AI;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerPromptType]
public sealed class HabitPrompts(
    BoardPersistenceService boards,
    ActivityStatisticsService stats,
    IHttpContextAccessor http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [McpServerPrompt(Name = "plan_day", Title = "Plan day"), Description("Plans today from dailies, todos, and today detail. Asks for a short ordered plan.")]
    public async Task<IEnumerable<ChatMessage>> PlanDay(
        [Description("Date as yyyy-MM-dd. Null means today.")] string? date = null,
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (tag is not null && tag.Length > McpInput.MaxTagsLength)
        {
            throw new McpProtocolException("Tag filter must be 500 chars or fewer.", McpErrorCode.InvalidParams);
        }

        var snapshot = await boards.GetSnapshotAsync(userId, 200, cancellationToken);
        DateOnly? day = null;
        if (date is not null)
        {
            day = McpInput.ParseDate(date);
        }

        var detail = day.HasValue
            ? await stats.GetActivityDayDetailAsync(userId, day.Value, tag, cancellationToken)
            : null;

        const int maxRows = 30;
        var dailies = snapshot.Dailies.Take(maxRows).Select(d => $"- {d.Title} (streak {d.Counter})").ToList();
        var todos = snapshot.Todos.Take(maxRows).Select(t => $"- {t.Title}").ToList();
        var truncated = snapshot.Dailies.Count > maxRows || snapshot.Todos.Count > maxRows
            ? $" (showing first {maxRows} of {snapshot.Dailies.Count} dailies and {snapshot.Todos.Count} todos)"
            : "";
        return [
            new(ChatRole.User,
                $"Plan my day{(day.HasValue ? $" for {day.Value:yyyy-MM-dd}" : "")}{(tag is null ? "" : $" tagged {tag}")}{truncated}.\n" +
                $"Dailies:\n{string.Join("\n", dailies)}\nTodos:\n{string.Join("\n", todos)}\n" +
                (detail is null ? "" : $"Today so far: {JsonSerializer.Serialize(detail, Json)}.") +
                "\nReply with the top 5 items in order and one line for each that says why.")
        ];
    }

    [McpServerPrompt(Name = "review_week", Title = "Review week"), Description("Reviews the last period: dashboard with habit and daily contributions. Asks for wins, gaps, and next week focus.")]
    public async Task<IEnumerable<ChatMessage>> ReviewWeek(
        [Description("Period key such as last_7_days or last_30_days.")] string? period = "last_7_days",
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (tag is not null && tag.Length > McpInput.MaxTagsLength)
        {
            throw new McpProtocolException("Tag filter must be 500 chars or fewer.", McpErrorCode.InvalidParams);
        }

        var overview = await stats.GetOverviewAsync(userId, period, tag, cancellationToken);
        return [
            new(ChatRole.User,
                $"Review my period {period ?? "default"}{(tag is null ? "" : $" tagged {tag}")}.\n" +
                $"Overview: {JsonSerializer.Serialize(overview, Json)}.\n" +
                "Reply with wins, gaps, and three focus items for next week.")
        ];
    }

    [McpServerPrompt(Name = "habit_coach", Title = "Habit coach"), Description("Coaches one habit: its counters and schedule. Asks for one small next step.")]
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
