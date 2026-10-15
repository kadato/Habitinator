using System.ComponentModel;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class ChecklistTools(BoardPersistenceService boards, IHttpContextAccessor http)
{
    [McpServerTool(Name = "get_checklist", Title = "Get checklist", ReadOnly = true, OpenWorld = false)]
    [Description("Reads the checklist subtasks of a habit, daily, or todo. Returns an empty list when it has none. Throws when the item is missing.")]
    public async Task<IReadOnlyList<DailyChecklistItem>> GetChecklist(
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        CancellationToken cancellationToken = default)
    {
        _ = section; // Kept for API compatibility. Board ids are globally unique.
        var current = await LoadItem(itemId, cancellationToken)
            ?? throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams);
        return DailyChecklistJson.Parse(current.ChecklistJson);
    }

    [McpServerTool(Name = "add_checklist_item", Title = "Add checklist item", OpenWorld = true)]
    [Description("Adds one subtask to the checklist of a habit, daily, or todo. Returns the updated checklist.")]
    public async Task<IReadOnlyList<DailyChecklistItem>> AddChecklistItem(
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        [Description("Subtask text, 1 to 500 chars.")] string text,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var current = await LoadItem(itemId, cancellationToken)
            ?? throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams);
        var items = DailyChecklistJson.Parse(current.ChecklistJson).ToList();
        items.Add(new DailyChecklistItem(Guid.NewGuid(), McpInput.CleanChecklistText(text), false));
        return await SaveChecklistAsync(userId, section, itemId, current, items, cancellationToken);
    }

    [McpServerTool(Name = "set_checklist_item_done", Title = "Set checklist item done", Idempotent = true, OpenWorld = true)]
    [Description("Checks or unchecks one subtask. Returns the updated checklist.")]
    public async Task<IReadOnlyList<DailyChecklistItem>> SetChecklistItemDone(
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        [Description("Subtask id.")] Guid checklistItemId,
        [Description("True to check, false to uncheck.")] bool done,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var current = await LoadItem(itemId, cancellationToken)
            ?? throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams);
        var items = DailyChecklistJson.Parse(current.ChecklistJson).ToList();
        var index = items.FindIndex(x => x.Id == checklistItemId);
        if (index < 0)
        {
            throw new McpProtocolException("Checklist item not found.", McpErrorCode.InvalidParams);
        }

        items[index] = items[index] with { IsDone = done };
        return await SaveChecklistAsync(userId, section, itemId, current, items, cancellationToken);
    }

    [McpServerTool(Name = "remove_checklist_item", Title = "Remove checklist item", OpenWorld = true)]
    [Description("Removes one subtask from the checklist. Returns the updated checklist.")]
    public async Task<IReadOnlyList<DailyChecklistItem>> RemoveChecklistItem(
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        [Description("Subtask id.")] Guid checklistItemId,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var current = await LoadItem(itemId, cancellationToken)
            ?? throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams);
        var items = DailyChecklistJson.Parse(current.ChecklistJson).ToList();
        if (items.RemoveAll(x => x.Id == checklistItemId) == 0)
        {
            throw new McpProtocolException("Checklist item not found.", McpErrorCode.InvalidParams);
        }

        return await SaveChecklistAsync(userId, section, itemId, current, items, cancellationToken);
    }

    [McpServerTool(Name = "set_checklist", Title = "Set checklist", Idempotent = true, OpenWorld = true)]
    [Description("Replaces the whole checklist with a JSON array of {text, isDone} objects. An empty array clears it. Returns the updated checklist.")]
    public async Task<IReadOnlyList<DailyChecklistItem>> SetChecklist(
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        [Description("JSON array like [{\"text\":\"Buy milk\",\"isDone\":false}].")]
        string json,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var current = await LoadItem(itemId, cancellationToken)
            ?? throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams);
        List<ChecklistEditRow> rows;
        try
        {
            rows = JsonSerializer.Deserialize<List<ChecklistEditRow>>(json, App.Shared.RCL.Services.JsonDefaults.Api) ?? [];
        }
        catch (JsonException)
        {
            throw new McpProtocolException("The json is not a valid checklist array.", McpErrorCode.InvalidParams);
        }

        var items = rows
            .Select(r => new DailyChecklistItem(Guid.NewGuid(), McpInput.CleanChecklistText(r.Text), r.IsDone))
            .ToList();
        return await SaveChecklistAsync(userId, section, itemId, current, items, cancellationToken);
    }

    private async Task<BoardItem?> LoadItem(Guid itemId, CancellationToken cancellationToken)
    {
        var userId = McpUser.RequireId(http);
        return await boards.GetItemAsync(userId, itemId, cancellationToken);
    }

    private async Task<IReadOnlyList<DailyChecklistItem>> SaveChecklistAsync(
        Guid userId,
        BoardSection section,
        Guid itemId,
        BoardItem current,
        List<DailyChecklistItem> items,
        CancellationToken cancellationToken)
    {
        var json = DailyChecklistJson.Serialize(items);
        if (json is not null && json.Length > McpInput.MaxChecklistLength)
        {
            throw new McpProtocolException("Checklist must be 8000 chars or fewer.", McpErrorCode.InvalidParams);
        }

        BoardItem? updated = section switch
        {
            BoardSection.Habit => (await boards.UpdateHabitAsync(
                userId, itemId, UpdateHabitArgs.From(current) with { ChecklistJson = json }, cancellationToken)).Item,
            BoardSection.Daily => (await boards.UpdateDailyAsync(
                userId, itemId, UpdateDailyArgs.From(current) with { ChecklistJson = json }, cancellationToken)).Item,
            _ => (await boards.UpdateTodoAsync(
                userId, itemId, UpdateTodoArgs.From(current) with { ChecklistJson = json }, cancellationToken)).Item,
        };
        return updated is null ? [] : DailyChecklistJson.Parse(updated.ChecklistJson);
    }

    private sealed record ChecklistEditRow(string Text, bool IsDone = false);
}
