using System.ComponentModel;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
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
        McpServer server,
        RequestContext<CallToolRequestParams> ctx,
        [Description("Max rows to return, 1 to 10000. Default 200 to keep payloads small. Null returns the cached full mirror.")]
        int? limit = 200,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        int? take = limit.HasValue ? Math.Clamp(limit.Value, 1, BoardPersistenceService.SnapshotMaxLimit) : null;
        await McpProgress.ReportAsync(server, ctx, 0, 1, "Reading snapshot.", cancellationToken);
        var snapshot = await boards.GetSnapshotAsync(userId, take, cancellationToken);
        await McpProgress.ReportAsync(server, ctx, 1, 1, "Snapshot done.", cancellationToken);
        return snapshot;
    }

    [McpServerTool(Name = "get_item", Title = "Get board item", ReadOnly = true, OpenWorld = false)]
    [Description("Reads one active board item by id. Throws when not found.")]
    public async Task<BoardItem> GetItem(
        [Description("Board item id as GUID string.")]
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        return await boards.GetItemAsync(userId, itemId, cancellationToken)
            ?? throw new McpProtocolException($"Item not found: {itemId:D}", McpErrorCode.InvalidParams);
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
        McpServer server,
        RequestContext<CallToolRequestParams> ctx,
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
        await McpProgress.ReportAsync(server, ctx, 0, 1, "Reading delta.", cancellationToken);
        var delta = await sync.GetSyncDeltaAsync(userId, cursor, take, cancellationToken);
        await McpProgress.ReportAsync(server, ctx, 1, 1, "Delta done.", cancellationToken);
        return delta;
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
        McpServer server,
        RequestContext<CallToolRequestParams> ctx,
        [Description("Free text matched against title, notes, and tags. Null matches all.")] string? query = null,
        [Description("Exact tag to match, or null for all.")] string? tag = null,
        [Description("Section to search, or null for all sections.")] BoardSection? section = null,
        [Description("Max rows to return, 1 to 200. Default 50.")]
        int? limit = 50,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        await McpProgress.ReportAsync(server, ctx, 0, 1, "Searching.", cancellationToken);
        var items = await boards.SearchItemsAsync(userId, query, tag, section, take, cancellationToken);
        await McpProgress.ReportAsync(server, ctx, 1, 1, "Search done.", cancellationToken);
        return items;
    }

    [McpServerTool(Name = "list_tags", Title = "List tags", ReadOnly = true, OpenWorld = false)]
    [Description("Lists distinct tags used across habits, dailies, and todos, sorted alphabetically.")]
    public async Task<string[]> ListTags(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        return await boards.GetDistinctTagsAsync(userId, cancellationToken);
    }

    [McpServerTool(Name = "reorder_item", Title = "Reorder board item", Idempotent = true, OpenWorld = true)]
    [Description("Moves an item to a zero-based position within its section list. Position 0 puts it first. Values past the end put it last. Returns the updated item. Throws when not found.")]
    public async Task<BoardItem> ReorderItem(
        McpServer server,
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        [Description("Zero-based position within the section list.")] int position,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var result = await boards.ReorderItemAsync(userId, section, itemId, position, cancellationToken);
        var item = result.Status switch
        {
            BoardMutationStatus.Ok when result.Item is not null => result.Item,
            BoardMutationStatus.NotFound => throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams),
            BoardMutationStatus.Conflict => throw new McpProtocolException(
                "Version conflict. Read the item again and retry.", McpErrorCode.InvalidRequest),
            _ => throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams),
        };
        await McpNotifications.BoardChangedAsync(server, item.Id, cancellationToken);
        return item;
    }

    [McpServerTool(Name = "create_item", Title = "Create board item", OpenWorld = true)]
    [Description("Creates a habit, daily, or todo with a title. Returns the created item.")]
    public async Task<BoardItem> CreateItem(
        McpServer server,
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

        var created = await boards.CreateItemDetailedAsync(userId, section, cleanTitle, notes, tags, parsedDate, null, cancellationToken);
        await McpNotifications.BoardChangedAsync(server, created.Id, cancellationToken);
        return created;
    }

    [McpServerTool(Name = "rename_item", Title = "Rename board item", Idempotent = true, OpenWorld = true)]
    [Description("Renames a board item. Returns the updated item. Throws when not found.")]
    public async Task<BoardItem> RenameItem(
        McpServer server,
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        [Description("New title, 1 to 200 chars.")] string title,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var cleanTitle = McpInput.CleanTitle(title);

        var result = await boards.RenameItemAsync(userId, section, itemId, cleanTitle, null, cancellationToken);
        var item = ThrowOnError(result);
        await McpNotifications.BoardChangedAsync(server, item.Id, cancellationToken);
        return item;
    }

    [McpServerTool(Name = "toggle_item", Title = "Toggle daily or todo", OpenWorld = true)]
    [Description("Toggles a daily or todo for today. You cannot uncomplete a past date. Completion there is append-only. For habits, use habit_plus and habit_minus instead. Returns the updated item.")]
    public async Task<BoardItem> ToggleItem(
        McpServer server,
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
        var item = ThrowOnError(result);
        await McpNotifications.BoardChangedAsync(server, item.Id, cancellationToken);
        return item;
    }

    [McpServerTool(Name = "archive_item", Title = "Archive board item", Idempotent = true, OpenWorld = true)]
    [Description("Archives an item so it hides from the active board. Returns the updated item.")]
    public async Task<BoardItem> ArchiveItem(
        McpServer server,
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var result = await boards.ArchiveItemAsync(userId, section, itemId, null, cancellationToken);
        var item = ThrowOnError(result);
        await McpNotifications.BoardChangedAsync(server, item.Id, cancellationToken);
        return item;
    }

    [McpServerTool(Name = "unarchive_item", Title = "Unarchive board item", Idempotent = true, OpenWorld = true)]
    [Description("Restores an archived item to the active board. Returns the updated item.")]
    public async Task<BoardItem> UnarchiveItem(
        McpServer server,
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var result = await boards.UnarchiveItemAsync(userId, section, itemId, null, cancellationToken);
        var item = ThrowOnError(result);
        await McpNotifications.BoardChangedAsync(server, item.Id, cancellationToken);
        return item;
    }

    [McpServerTool(Name = "delete_item", Title = "Delete board item", Destructive = true, OpenWorld = true)]
    [Description("Soft deletes an item. The row hides from the board. Creating the item again with the same id restores it. Ask the user for confirmation first, then retry with confirmToken set to the item id string.")]
    public async Task<string> DeleteItem(
        McpServer server,
        RequestContext<CallToolRequestParams> ctx,
        [Description("Section of the item.")] BoardSection section,
        [Description("Item id.")] Guid itemId,
        [Description("Confirmation token. Pass the item id string to confirm. Omit on the first call.")]
        string? confirmToken = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (!string.Equals(confirmToken, itemId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            && !ConfirmDeleteAccepted(ctx))
        {
            if (server.IsMrtrSupported)
            {
                throw NeedDeleteConfirm(section, itemId);
            }

            throw new McpProtocolException(
                $"Delete needs confirmation. Retry with confirmToken set to '{itemId:D}'.",
                McpErrorCode.InvalidParams);
        }

        var result = await boards.DeleteItemAsync(userId, section, itemId, null, cancellationToken);
        if (result.Status == BoardMutationStatus.Ok)
        {
            await McpNotifications.BoardChangedAsync(server, itemId, cancellationToken);
            return $"Deleted {itemId:D}.";
        }

        if (result.Status == BoardMutationStatus.NotFound)
        {
            throw new McpProtocolException(
                $"Item not found: {itemId:D}", McpErrorCode.InvalidParams);
        }

        throw new McpProtocolException(
            "Version conflict. Read the item again and retry.", McpErrorCode.InvalidRequest);
    }

    private static BoardItem ThrowOnError(BoardMutationResult result) =>
        result.Status switch
        {
            BoardMutationStatus.Ok when result.Item is not null => result.Item,
            BoardMutationStatus.NotFound => throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams),
            BoardMutationStatus.Conflict => throw new McpProtocolException(
                "Version conflict. Read the item again and retry.", McpErrorCode.InvalidRequest),
            _ => throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams),
        };

    private static bool ConfirmDeleteAccepted(RequestContext<CallToolRequestParams> ctx)
    {
        if (ctx.Params?.InputResponses is null
            || !ctx.Params.InputResponses.TryGetValue("confirm_delete", out var resp))
        {
            return false;
        }

        if (resp is null)
        {
            return false;
        }

        var elicit = resp.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
        if (elicit is null || !elicit.IsAccepted || elicit.Content is null)
        {
            return false;
        }

        return elicit.Content.TryGetValue("confirm", out var value)
            && value.ValueKind == JsonValueKind.True;
    }

    private static InputRequiredException NeedDeleteConfirm(BoardSection section, Guid itemId) =>
        new(new Dictionary<string, InputRequest>
        {
            ["confirm_delete"] = InputRequest.ForElicitation(new ElicitRequestParams
            {
                Message = $"Delete {section} '{itemId:D}'? This hides it from the board.",
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        ["confirm"] = new ElicitRequestParams.BooleanSchema
                        {
                            Description = "Confirm delete",
                            Default = false,
                        },
                    },
                    Required = ["confirm"],
                },
            }),
        }, requestState: JsonSerializer.Serialize(new { section = section.ToString(), itemId }));
}
