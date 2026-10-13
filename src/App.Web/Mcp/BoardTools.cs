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
    [McpServerTool(Name = "get_snapshot", Title = "Get board snapshot", ReadOnly = true, OpenWorld = false)]
    [Description("Reads the full board mirror: habits, dailies, and todos. Before any mutation, read this so you have item ids.")]
    public async Task<BoardSnapshot> GetSnapshot(
        [Description("Max rows to return, 1 to 10000. Default 200 to keep payloads small. Null returns the cached full mirror.")]
        int? limit = 200,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        int? take = limit.HasValue ? Math.Clamp(limit.Value, 1, BoardPersistenceService.SnapshotMaxLimit) : null;
        return await boards.GetSnapshotAsync(userId, take, cancellationToken);
    }

    [McpServerTool(Name = "get_item", Title = "Get board item", ReadOnly = true, OpenWorld = false)]
    [Description("Reads one active board item by id. Returns null when not found.")]
    public async Task<BoardItem?> GetItem(
        [Description("Board item id as GUID string.")]
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        return await boards.GetItemAsync(userId, itemId, cancellationToken);
    }

    [McpServerTool(Name = "get_archived", Title = "Get archived items", ReadOnly = true, OpenWorld = false)]
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

    [McpServerTool(Name = "get_sync_delta", Title = "Get board sync delta", ReadOnly = true, OpenWorld = false)]
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

    [McpServerTool(Name = "get_streaks", Title = "Get daily streaks", ReadOnly = true, OpenWorld = false)]
    [Description("Reads the daily streak map: item id to current streak count.")]
    public async Task<Dictionary<Guid, int>> GetStreaks(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        return await boards.GetDailyStreakMapAsync(userId, cancellationToken);
    }

    [McpServerTool(Name = "search_items", Title = "Search board items", ReadOnly = true, OpenWorld = false)]
    [Description("Searches active board items by title, exact tag, and section. Use this instead of pulling the full snapshot to find one item.")]
    public async Task<IReadOnlyList<BoardItem>> SearchItems(
        [Description("Free text matched against title, notes, and tags. Null matches all.")] string? query = null,
        [Description("Exact tag to match, or null for all.")] string? tag = null,
        [Description("Section to search, or null for all sections.")] BoardSection? section = null,
        [Description("Max rows to return, 1 to 200. Default 50.")]
        int? limit = 50,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var snapshot = await boards.GetSnapshotAsync(userId, null, cancellationToken);
        IEnumerable<BoardItem> all = section switch
        {
            BoardSection.Habit => snapshot.Habits,
            BoardSection.Daily => snapshot.Dailies,
            BoardSection.Todo => snapshot.Todos,
            _ => snapshot.Habits.Concat(snapshot.Dailies).Concat(snapshot.Todos),
        };

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var wanted = tag.Trim();
            all = all.Where(x => BoardTagUtil.ParseTags(x.Tags)
                .Any(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            all = all.Where(x =>
                (x.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.Notes?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.Tags?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return all.Take(take).ToList();
    }

    [McpServerTool(Name = "list_tags", Title = "List tags", ReadOnly = true, OpenWorld = false)]
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

    [McpServerTool(Name = "reorder_item", Title = "Reorder board item", Idempotent = true, OpenWorld = true)]
    [Description("Moves an item to a zero-based position within its section list. Position 0 puts it first. Values past the end put it last. Returns the updated item.")]
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

    [McpServerTool(Name = "create_item", Title = "Create board item", OpenWorld = true)]
    [Description("Creates a habit, daily, or todo with a title. Returns the created item.")]
    public async Task<BoardItem> CreateItem(
        [Description("Section: Habit, Daily, or Todo.")]
        BoardSection section,
        [Description("Title, 1 to 200 chars.")]
        string title,
        [Description("Notes up to 4000 chars, or null.")] string? notes = null,
        [Description("Comma-separated tags up to 500 chars, or null.")] string? tags = null,
        [Description("Date as yyyy-MM-dd. For Daily it sets the start date. For Todo it sets the due date. Omit the date for Habit.")]
        string? date = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var cleanTitle = McpInput.CleanTitle(title);
        McpInput.CheckNotes(notes);
        McpInput.CheckTags(tags);

        DateOnly? parsedDate = null;
        if (date is not null)
        {
            if (section == BoardSection.Habit)
            {
                throw new McpProtocolException("Habits take no date. Omit the date for Habit.", McpErrorCode.InvalidParams);
            }

            parsedDate = McpInput.ParseDate(date);
        }

        var created = await boards.CreateItemAsync(userId, section, cleanTitle, null, cancellationToken);
        if (notes is null && tags is null && parsedDate is null)
        {
            return created;
        }

        if (section == BoardSection.Todo)
        {
            var todoArgs = UpdateTodoArgs.From(created) with { Notes = notes, Tags = tags, DueDate = parsedDate, };
            return (await boards.UpdateTodoAsync(userId, created.Id, todoArgs, cancellationToken)).Item ?? created;
        }

        if (section == BoardSection.Daily)
        {
            var dailyArgs = UpdateDailyArgs.From(created) with { Notes = notes, Tags = tags, StartDate = parsedDate, };
            return (await boards.UpdateDailyAsync(userId, created.Id, dailyArgs, cancellationToken)).Item ?? created;
        }

        var habitArgs = UpdateHabitArgs.From(created) with { Notes = notes, Tags = tags, };
        return (await boards.UpdateHabitAsync(userId, created.Id, habitArgs, cancellationToken)).Item ?? created;
    }

    [McpServerTool(Name = "rename_item", Title = "Rename board item", Idempotent = true, OpenWorld = true)]
    [Description("Renames a board item. Returns the updated item or null when not found.")]
    public async Task<BoardItem?> RenameItem(
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        [Description("New title, 1 to 200 chars.")] string title,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var cleanTitle = McpInput.CleanTitle(title);

        var result = await boards.RenameItemAsync(userId, section, itemId, cleanTitle, null, cancellationToken);
        return result.Item;
    }

    [McpServerTool(Name = "toggle_item", Title = "Toggle daily or todo", OpenWorld = true)]
    [Description("Toggles a daily or todo for today. You cannot uncomplete a past date. Completion there is append-only. For habits, use habit_plus and habit_minus instead. Returns the updated item.")]
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

    [McpServerTool(Name = "archive_item", Title = "Archive board item", Idempotent = true, OpenWorld = true)]
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

    [McpServerTool(Name = "unarchive_item", Title = "Unarchive board item", Idempotent = true, OpenWorld = true)]
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

    [McpServerTool(Name = "delete_item", Title = "Delete board item", Destructive = true, OpenWorld = true)]
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
