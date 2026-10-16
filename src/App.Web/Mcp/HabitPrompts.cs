using System.ComponentModel;
using System.Text.Json;

using App.Shared.RCL.Models;
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
        IEnumerable<BoardItem> dailiesSource = snapshot.Dailies;
        IEnumerable<BoardItem> todosSource = snapshot.Todos;
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var wanted = tag.Trim();
            dailiesSource = dailiesSource.Where(d => BoardTagUtil.ParseTags(d.Tags)
                .Any(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase)));
            todosSource = todosSource.Where(t => BoardTagUtil.ParseTags(t.Tags)
                .Any(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase)));
        }

        var dailies = dailiesSource.Take(maxRows).Select(d => $"- {d.Title} (streak {d.Counter})").ToList();
        var todos = todosSource.Take(maxRows).Select(t => $"- {t.Title}").ToList();
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
            throw new McpProtocolException("habitId must be a GUID.", McpErrorCode.InvalidParams);
        }

        var item = await boards.GetItemAsync(userId, id, cancellationToken)
            ?? throw new McpProtocolException($"Habit not found: {habitId}", McpErrorCode.InvalidParams);

        return [
            new(ChatRole.User,
                $"Coach my habit '{item.Title}': plus {item.Counter}, minus {item.NegativeCounter}, reset {item.ResetPeriod}.\n" +
                "Reply with one small next step I can do today.")
        ];
    }

    [McpServerPrompt(Name = "daily_standup", Title = "Daily standup"), Description("Morning standup: yesterday's activity, today's dailies, overdue and upcoming todos, and active habits. Suggests today's focus.")]
    public async Task<IEnumerable<ChatMessage>> DailyStandup(
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
        DateOnly today = date is not null ? McpInput.ParseDate(date) : DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        var yesterdayDetail = await stats.GetActivityDayDetailAsync(userId, yesterday, tag, cancellationToken);
        var yesterdayCompleted = yesterdayDetail.Events.Count(e => e.EventType is ActivityEventType.DailyComplete or ActivityEventType.TodoComplete);

        IEnumerable<BoardItem> habits = snapshot.Habits;
        IEnumerable<BoardItem> dailies = snapshot.Dailies;
        IEnumerable<BoardItem> todos = snapshot.Todos;

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var wanted = tag.Trim();
            habits = habits.Where(h => BoardTagUtil.ParseTags(h.Tags).Any(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase)));
            dailies = dailies.Where(d => BoardTagUtil.ParseTags(d.Tags).Any(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase)));
            todos = todos.Where(t => BoardTagUtil.ParseTags(t.Tags).Any(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase)));
        }

        var pendingDailies = dailies.Where(d => !d.IsCompleted).Select(d => $"- {d.Title} (streak {d.Counter})");
        var activeTodos = todos.Where(t => !t.IsCompleted).Select(t => $"- {t.Title}{(t.TodoDueDate.HasValue ? $" [Due: {t.TodoDueDate.Value:yyyy-MM-dd}]" : "")}");
        var habitList = habits.Select(h => $"- {h.Title} (+{h.Counter}/-{h.NegativeCounter})");

        return [
            new(ChatRole.User,
                $"Daily standup for {today:yyyy-MM-dd}{(tag is null ? "" : $" [Tag: {tag}]")}.\n" +
                $"Yesterday's summary: {yesterdayCompleted} completed items, {yesterdayDetail.Events.Count} total events.\n" +
                $"Today's pending dailies:\n{string.Join("\n", pendingDailies)}\n" +
                $"Pending todos:\n{string.Join("\n", activeTodos)}\n" +
                $"Active habits:\n{string.Join("\n", habitList)}\n" +
                "Act as my agile coach. Reply with: 1) Top 3 focus priorities for today, 2) Quick wins under 15 minutes, and 3) Potential bottlenecks or habits to watch.")
        ];
    }

    [McpServerPrompt(Name = "evening_review", Title = "Evening review"), Description("Evening reflection: checks today's completed and pending items, habit counters, and prepares for tomorrow.")]
    public async Task<IEnumerable<ChatMessage>> EveningReview(
        [Description("Date as yyyy-MM-dd. Null means today.")] string? date = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        DateOnly day = date is not null ? McpInput.ParseDate(date) : DateOnly.FromDateTime(DateTime.UtcNow);
        var detail = await stats.GetActivityDayDetailAsync(userId, day, null, cancellationToken);
        var snapshot = await boards.GetSnapshotAsync(userId, 200, cancellationToken);

        var loggedEvents = detail.Events.Select(e => $"- {e.Label}: {e.BoardItemTitle ?? e.CustomLabel ?? "item"}");
        var missedDailies = snapshot.Dailies.Where(d => !d.IsCompleted).Select(d => $"- {d.Title}");

        return [
            new(ChatRole.User,
                $"Evening review for {day:yyyy-MM-dd}.\n" +
                $"Logged today ({detail.Events.Count} events, {detail.FocusMinutesTotal} focus minutes):\n{string.Join("\n", loggedEvents)}\n" +
                $"Unfinished dailies:\n{string.Join("\n", missedDailies)}\n" +
                "Review my day kindly but honestly. Highlight wins, ask why unfinished dailies were missed, and suggest one adjustment for tomorrow morning.")
        ];
    }

    [McpServerPrompt(Name = "backlog_triage", Title = "Backlog triage"), Description("Triages stale todos, untagged items, and broken streaks with recommendations to schedule, break down, or archive.")]
    public async Task<IEnumerable<ChatMessage>> BacklogTriage(
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (tag is not null && tag.Length > McpInput.MaxTagsLength)
        {
            throw new McpProtocolException("Tag filter must be 500 chars or fewer.", McpErrorCode.InvalidParams);
        }

        var snapshot = await boards.GetSnapshotAsync(userId, 200, cancellationToken);
        var todos = snapshot.Todos.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var wanted = tag.Trim();
            todos = todos.Where(t => BoardTagUtil.ParseTags(t.Tags).Any(x => string.Equals(x, wanted, StringComparison.OrdinalIgnoreCase)));
        }

        var activeTodos = todos.Where(t => !t.IsCompleted).ToList();
        var completedTodos = todos.Where(t => t.IsCompleted).ToList();
        var untagged = activeTodos.Where(t => string.IsNullOrWhiteSpace(t.Tags)).Select(t => t.Title).Take(15);
        var withChecklists = activeTodos.Where(t => !string.IsNullOrWhiteSpace(t.ChecklistJson)).Select(t => t.Title).Take(10);
        var overdue = activeTodos.Where(t => t.TodoDueDate.HasValue && t.TodoDueDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)).Select(t => $"{t.Title} (due {t.TodoDueDate!.Value:yyyy-MM-dd})");

        return [
            new(ChatRole.User,
                $"Backlog triage{(tag is null ? "" : $" for tag {tag}")}.\n" +
                $"Active todos: {activeTodos.Count}, Completed unarchived todos: {completedTodos.Count}.\n" +
                $"Overdue todos:\n{string.Join("\n", overdue)}\n" +
                $"Untagged todos sample:\n{string.Join("\n", untagged)}\n" +
                $"Sample todos with checklists:\n{string.Join("\n", withChecklists)}\n" +
                "Provide triage recommendations: 1) Items ready to archive, 2) Overdue items needing rescheduling or breakdown, 3) Suggested tags for untagged items.")
        ];
    }
}
