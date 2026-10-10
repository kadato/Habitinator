using System.ComponentModel;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class BoardTools(
    BoardPersistenceService boards,
    BoardSyncDeltaService sync,
    IHttpContextAccessor http)
{
    [McpServerTool(Name = "get_snapshot", ReadOnly = true, OpenWorld = false)]
    [Description("Reads the full board mirror: habits, dailies, todos. Call this first before any mutation so you have item ids.")]
    public async Task<BoardSnapshot> GetSnapshot(
        [Description("Max rows to return, 1 to 10000. Default 200 to keep payloads small. Null returns the cached full mirror.")]
        int? limit = 200,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        int? take = limit.HasValue ? Math.Clamp(limit.Value, 1, BoardPersistenceService.SnapshotMaxLimit) : null;
        return await boards.GetSnapshotAsync(userId, take, cancellationToken);
    }

    [McpServerTool(Name = "get_item", ReadOnly = true, OpenWorld = false)]
    [Description("Reads one active board item by id. Returns null when not found.")]
    public async Task<BoardItem?> GetItem(
        [Description("Board item id as GUID string.")]
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        return await boards.GetItemAsync(userId, itemId, cancellationToken);
    }

    [McpServerTool(Name = "get_archived", ReadOnly = true, OpenWorld = false)]
    [Description("Reads archived items hidden from the active board.")]
    public async Task<BoardSnapshot> GetArchived(
        [Description("Max rows to return, 1 to 10000. Default 200.")]
        int? limit = 200,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        int? take = limit.HasValue ? Math.Clamp(limit.Value, 1, BoardPersistenceService.SnapshotMaxLimit) : null;
        return await boards.GetArchivedSnapshotAsync(userId, take, cancellationToken);
    }

    [McpServerTool(Name = "get_sync_delta", ReadOnly = true, OpenWorld = false)]
    [Description("Reads incremental board changes after a cursor. Use for sync, not for the first read. Cursor is an ISO 8601 watermark or watermark|id composite from a prior call.")]
    public async Task<BoardSyncDelta> GetSyncDelta(
        [Description("Cursor from the NextCursor of the last call. Required.")]
        string cursor,
        [Description("Max rows per page, 1 to 5000. Default 200.")]
        int? limit = 200,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (string.IsNullOrWhiteSpace(cursor))
        {
            throw new McpProtocolException("Missing required input: cursor.", McpErrorCode.InvalidParams);
        }

        int? take = limit.HasValue ? Math.Clamp(limit.Value, 1, BoardSyncDeltaService.SyncMaxPageSize) : null;
        return await sync.GetSyncDeltaAsync(userId, cursor, take, cancellationToken);
    }

    [McpServerTool(Name = "get_streaks", ReadOnly = true, OpenWorld = false)]
    [Description("Reads the daily streak map: item id to current streak count.")]
    public async Task<Dictionary<Guid, int>> GetStreaks(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        return await boards.GetDailyStreakMapAsync(userId, cancellationToken);
    }

    [McpServerTool(Name = "list_tags", ReadOnly = true, OpenWorld = false)]
    [Description("Lists distinct tags used across habits, dailies, and todos, sorted alphabetically.")]
    public async Task<string[]> ListTags(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var snapshot = await boards.GetSnapshotAsync(userId, null, cancellationToken);
        return [.. snapshot.Habits.Concat(snapshot.Dailies).Concat(snapshot.Todos)
            .SelectMany(x => BoardTagUtil.ParseTags(x.Tags))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
    }

    [McpServerTool(Name = "reorder_item", OpenWorld = true)]
    [Description("Moves an item to a zero-based position within its section list. Position 0 puts it first; values past the end put it last. Returns the updated item.")]
    public async Task<BoardItem?> ReorderItem(
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        [Description("Zero-based position within the section list.")] int position,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var snapshot = await boards.GetSnapshotAsync(userId, null, cancellationToken);
        var list = (section switch
        {
            BoardSection.Habit => snapshot.Habits,
            BoardSection.Daily => snapshot.Dailies,
            _ => snapshot.Todos,
        }).ToList();
        var currentIndex = list.FindIndex(x => x.Id == itemId);
        if (currentIndex < 0)
        {
            return null;
        }

        var moving = list[currentIndex];
        list.RemoveAt(currentIndex);
        var insertAt = Math.Clamp(position, 0, list.Count);
        list.Insert(insertAt, moving);
        var sortOrder = BoardItemReorder.ComputeMidpointSortOrder(list, insertAt, static x => x.SortOrder);

        return section switch
        {
            BoardSection.Habit => (await boards.UpdateHabitAsync(
                userId, itemId, UpdateHabitArgs.From(moving) with { SortOrder = sortOrder }, cancellationToken)).Item,
            BoardSection.Daily => (await boards.UpdateDailyAsync(
                userId, itemId, UpdateDailyArgs.From(moving) with { SortOrder = sortOrder }, cancellationToken)).Item,
            _ => (await boards.UpdateTodoAsync(
                userId, itemId, UpdateTodoArgs.From(moving) with { SortOrder = sortOrder }, cancellationToken)).Item,
        };
    }

    [McpServerTool(Name = "create_item", OpenWorld = true)]
    [Description("Creates a habit, daily, or todo with a title. Returns the created item.")]
    public async Task<BoardItem> CreateItem(
        [Description("Section: Habit, Daily, or Todo.")]
        BoardSection section,
        [Description("Title, 1 to 200 chars.")]
        string title,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            throw new McpProtocolException("Title must be 1 to 200 chars.", McpErrorCode.InvalidParams);
        }

        return await boards.CreateItemAsync(userId, section, title.Trim(), null, cancellationToken);
    }

    [McpServerTool(Name = "rename_item", OpenWorld = true)]
    [Description("Renames a board item. Returns the updated item or null when not found.")]
    public async Task<BoardItem?> RenameItem(
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        [Description("New title, 1 to 200 chars.")] string title,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            throw new McpProtocolException("Title must be 1 to 200 chars.", McpErrorCode.InvalidParams);
        }

        var result = await boards.RenameItemAsync(userId, section, itemId, title.Trim(), null, cancellationToken);
        return result.Item;
    }

    [McpServerTool(Name = "toggle_item", OpenWorld = true)]
    [Description("Toggles a daily or todo for today. Habits use habit_plus and habit_minus instead. Returns the updated item.")]
    public async Task<BoardItem?> ToggleItem(
        [Description("Section: Daily or Todo.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (section == BoardSection.Habit)
        {
            throw new McpProtocolException("Habits cannot be toggled. Use habit_plus or habit_minus.", McpErrorCode.InvalidParams);
        }

        var result = await boards.ToggleItemAsync(userId, section, itemId, null, cancellationToken);
        return result.Item;
    }

    [McpServerTool(Name = "archive_item", OpenWorld = true)]
    [Description("Archives an item so it hides from the active board. Returns the updated item.")]
    public async Task<BoardItem?> ArchiveItem(
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var result = await boards.ArchiveItemAsync(userId, section, itemId, null, cancellationToken);
        return result.Item;
    }

    [McpServerTool(Name = "unarchive_item", OpenWorld = true)]
    [Description("Restores an archived item to the active board. Returns the updated item.")]
    public async Task<BoardItem?> UnarchiveItem(
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var result = await boards.UnarchiveItemAsync(userId, section, itemId, null, cancellationToken);
        return result.Item;
    }

    [McpServerTool(Name = "delete_item", Destructive = true, OpenWorld = true)]
    [Description("Soft deletes an item. The row hides from the board. Creating the item again with the same id restores it. Ask the user for confirmation first, then retry with confirmToken set to the item id string.")]
    public async Task<string> DeleteItem(
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        [Description("Confirmation token. Pass the item id string to confirm. Omit on the first call.")]
        string? confirmToken = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (!string.Equals(confirmToken, itemId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            throw new McpProtocolException(
                $"Delete needs confirmation. Retry with confirmToken set to '{itemId:D}'.",
                McpErrorCode.InvalidParams);
        }

        var result = await boards.DeleteItemAsync(userId, section, itemId, null, cancellationToken);
        return result.Status == BoardMutationStatus.Ok
            ? $"Deleted {itemId:D}."
            : $"Item {itemId:D} not found. No change made.";
    }
}
