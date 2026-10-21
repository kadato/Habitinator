using System.ComponentModel;
using System.Globalization;
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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "get_board_summary", Title = "Get board summary", ReadOnly = true, OpenWorld = false)]
    [Description("Returns a summary of active habits, completed and pending dailies, active and overdue todos, and streaks. Output format is markdown or json.")]
    public async Task<string> GetBoardSummary(
        [Description("Output format: markdown or json. Defaults to markdown.")] string? format = "markdown",
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var snapshot = await boards.GetSnapshotAsync(userId, 200, cancellationToken);
        var today = await boards.TodayAsync(userId, cancellationToken);

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            var summary = new
            {
                HabitsCount = snapshot.Habits.Count,
                TotalHabitTicks = snapshot.Habits.Sum(h => h.Counter),
                DailiesCount = snapshot.Dailies.Count,
                DailiesCompletedToday = snapshot.Dailies.Count(d => d.IsCompleted),
                DailiesPendingToday = snapshot.Dailies.Count(d => !d.IsCompleted),
                TodosActiveCount = snapshot.Todos.Count(t => !t.IsCompleted),
                TodosCompletedCount = snapshot.Todos.Count(t => t.IsCompleted),
                TodosOverdueCount = snapshot.Todos.Count(t => !t.IsCompleted && t.TodoDueDate.HasValue && t.TodoDueDate.Value < today),
                TopStreaks = snapshot.Dailies
                    .Where(d => d.Counter > 0)
                    .OrderByDescending(d => d.Counter)
                    .Take(5)
                    .Select(d => new { d.Title, Streak = d.Counter })
                    .ToList()
            };
            return JsonSerializer.Serialize(summary, Json);
        }

        var pendingDailies = snapshot.Dailies.Where(d => !d.IsCompleted).ToList();
        var doneDailies = snapshot.Dailies.Where(d => d.IsCompleted).ToList();
        var overdueTodos = snapshot.Todos.Where(t => !t.IsCompleted && t.TodoDueDate.HasValue && t.TodoDueDate.Value < today).ToList();
        var activeTodos = snapshot.Todos.Where(t => !t.IsCompleted && (!t.TodoDueDate.HasValue || t.TodoDueDate.Value >= today)).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Board Summary ({today:yyyy-MM-dd})");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Dailies:** {doneDailies.Count}/{snapshot.Dailies.Count} done");
        if (pendingDailies.Count > 0)
        {
            sb.AppendLine("### Pending Dailies");
            foreach (var d in pendingDailies.Take(10))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {d.Title} (streak: {d.Counter})");
            }
        }
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Todos:** {activeTodos.Count} active, {overdueTodos.Count} overdue");
        if (overdueTodos.Count > 0)
        {
            sb.AppendLine("### Overdue Todos");
            foreach (var t in overdueTodos.Take(10))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- [!] {t.Title} (due: {t.TodoDueDate!.Value:yyyy-MM-dd})");
            }
        }
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Habits:** {snapshot.Habits.Count} tracked");
        foreach (var h in snapshot.Habits.Take(5))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {h.Title}: +{h.Counter} / -{h.NegativeCounter}");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_snapshot", Title = "Get board snapshot", ReadOnly = true, OpenWorld = false)]
    [Description("Reads the full board mirror: habits, dailies, and todos. Read this to get item IDs before mutations. Output format is json or markdown.")]
    public async Task<object> GetSnapshot(
        McpServer server,
        RequestContext<CallToolRequestParams> ctx,
        [Description("Max rows to return, 1 to 10000. Default 200 to keep payloads small. Null returns the cached full mirror.")]
        int? limit = 200,
        [Description("Output format: json or markdown. Defaults to json.")]
        string? format = "json",
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        int? take = limit.HasValue ? Math.Clamp(limit.Value, 1, BoardPersistenceService.SnapshotMaxLimit) : null;
        await McpProgress.ReportAsync(server, ctx, 0, 1, "Reading snapshot.", cancellationToken);
        var snapshot = await boards.GetSnapshotAsync(userId, take, cancellationToken);
        await McpProgress.ReportAsync(server, ctx, 1, 1, "Snapshot done.", cancellationToken);

        if (string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase))
        {
            var today = await boards.TodayAsync(userId, cancellationToken);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"# Board Snapshot ({today:yyyy-MM-dd})");
            sb.AppendLine();
            sb.AppendLine("### Habits");
            foreach (var h in snapshot.Habits)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- `{h.Id:D}` **{h.Title}** (+{h.Counter}/-{h.NegativeCounter}){(string.IsNullOrWhiteSpace(h.Tags) ? "" : $" [Tags: {h.Tags}]")}");
            }
            sb.AppendLine();
            sb.AppendLine("### Dailies");
            foreach (var d in snapshot.Dailies)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- `{d.Id:D}` [{(d.IsCompleted ? "x" : " ")}] **{d.Title}** (streak: {d.Counter}){(string.IsNullOrWhiteSpace(d.Tags) ? "" : $" [Tags: {d.Tags}]")}");
            }
            sb.AppendLine();
            sb.AppendLine("### Todos");
            foreach (var t in snapshot.Todos)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- `{t.Id:D}` [{(t.IsCompleted ? "x" : " ")}] **{t.Title}**{(t.TodoDueDate.HasValue ? $" (due: {t.TodoDueDate.Value:yyyy-MM-dd})" : "")}{(string.IsNullOrWhiteSpace(t.Tags) ? "" : $" [Tags: {t.Tags}]")}");
            }
            return sb.ToString();
        }

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
    [Description("Searches board items by title, exact tag, and section. Use this to find an item without pulling the full snapshot. Output format is json or markdown.")]
    public async Task<object> SearchItems(
        McpServer server,
        RequestContext<CallToolRequestParams> ctx,
        [Description("Free text matched against title, notes, and tags. Null matches all.")] string? query = null,
        [Description("Exact tag to match, or null for all.")] string? tag = null,
        [Description("Section to search, or null for all sections.")] BoardSection? section = null,
        [Description("Max rows to return, 1 to 200. Default 50.")]
        int? limit = 50,
        [Description("Include archived items in search results. Default false.")]
        bool includeArchived = false,
        [Description("Output format: json or markdown. Defaults to json.")]
        string? format = "json",
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        await McpProgress.ReportAsync(server, ctx, 0, 1, "Searching.", cancellationToken);
        var items = await boards.SearchItemsAsync(userId, query, tag, section, take, includeArchived, cancellationToken);
        await McpProgress.ReportAsync(server, ctx, 1, 1, "Search done.", cancellationToken);

        if (string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"# Search Results ({items.Count} items)");
            sb.AppendLine();
            foreach (var item in items)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- `{item.Id:D}` **{item.Title}** [{(item.IsCompleted ? "done" : "open")}]{(string.IsNullOrWhiteSpace(item.Tags) ? "" : $" (tags: {item.Tags})")}{(item.IsArchived ? " [Archived]" : "")}");
            }
            return sb.ToString();
        }

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
        [Description("Item id.")] Guid itemId,
        [Description("Zero-based position within the section list.")] int position,
        [Description("Section of the item. Optional; auto-detected if omitted.")] BoardSection? section = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var resolvedSection = section ?? await boards.GetItemSectionAsync(userId, itemId, cancellationToken)
            ?? throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams);
        var result = await boards.ReorderItemAsync(userId, resolvedSection, itemId, position, cancellationToken);
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
    [Description("Creates a habit, daily, or todo with title, schedule, tracking, and optional checklist. Returns the created item.")]
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
        [Description("Checklist as JSON array of items or checklist text, 8000 chars or fewer, or null.")]
        string? checklistJson = null,
        [Description("Repeat schedule for Daily: Daily, Weekly, Monthly, or Yearly, or null.")] DailyRepeatType? repeat = null,
        [Description("Repeat interval for Daily, 1 to 999, or null.")] int? repeatInterval = null,
        [Description("Weekday bitmask for weekly Daily, or null.")] int? weekdays = null,
        [Description("Weekday names like [\"Mon\", \"Wed\", \"Fri\"] for weekly Daily, or null.")] IReadOnlyList<string>? weekdayNames = null,
        [Description("Habit reset period: Daily, Weekly, or Monthly, or null.")] HabitResetPeriod? resetPeriod = null,
        [Description("Track plus ticks for Habit, or null.")] bool? trackPlus = null,
        [Description("Track minus ticks for Habit, or null.")] bool? trackMinus = null,
        [Description("Initial streak counter or habit plus tally, or null.")] int? counter = null,
        [Description("Initial habit minus tally, or null.")] int? negativeCounter = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var cleanTitle = McpInput.CleanTitle(title);
        McpInput.CheckNotes(notes);
        McpInput.CheckTags(tags);
        var normalizedChecklist = checklistJson is not null ? McpInput.NormalizedChecklist(checklistJson) : null;

        DateOnly? parsedDate = null;
        if (date is not null)
        {
            if (section == BoardSection.Habit)
            {
                throw new McpProtocolException("Habits take no date. Omit the date for Habit.", McpErrorCode.InvalidParams);
            }

            parsedDate = McpInput.ParseDate(date);
        }

        var resolvedRepeatInterval = repeatInterval.HasValue ? McpInput.ResolveRepeatInterval(repeatInterval, 1) : (int?)null;
        var resolvedWeekdays = (weekdays.HasValue || weekdayNames is { Count: > 0 })
            ? McpInput.ResolveWeekdaysWithNames(weekdays, weekdayNames, 0)
            : (int?)null;
        var resolvedCounter = counter.HasValue ? McpInput.ResolveDailyCounter(counter, 0) : (int?)null;
        var resolvedNegCounter = negativeCounter.HasValue ? McpInput.ResolveHabitCounter(negativeCounter, 0, "Negative counter") : (int?)null;

        var created = await boards.CreateItemDetailedAsync(
            userId,
            section,
            cleanTitle,
            notes,
            tags,
            parsedDate,
            null,
            normalizedChecklist,
            repeat,
            resolvedRepeatInterval,
            resolvedWeekdays,
            resetPeriod,
            trackPlus,
            trackMinus,
            resolvedCounter,
            resolvedNegCounter,
            cancellationToken);
        await McpNotifications.BoardChangedAsync(server, created.Id, cancellationToken);
        return created;
    }

    public sealed record BatchCreateItemInput(
        [Description("Section: Habit, Daily, or Todo.")] BoardSection Section,
        [Description("Title, 1 to 200 chars.")] string Title,
        [Description("Notes up to 4000 chars, or null.")] string? Notes = null,
        [Description("Comma-separated tags up to 500 chars, or null.")] string? Tags = null,
        [Description("Date as yyyy-MM-dd. Start date for Daily, due date for Todo. Omit for Habit.")] string? Date = null,
        [Description("Checklist as JSON array or text, or null.")] string? ChecklistJson = null,
        [Description("Repeat schedule for Daily: Daily, Weekly, Monthly, or Yearly, or null.")] DailyRepeatType? Repeat = null,
        [Description("Repeat interval for Daily, 1 to 999, or null.")] int? RepeatInterval = null,
        [Description("Weekday names like [\"Mon\", \"Wed\", \"Fri\"] for weekly Daily, or null.")] IReadOnlyList<string>? WeekdayNames = null,
        [Description("Habit reset period: Daily, Weekly, or Monthly, or null.")] HabitResetPeriod? ResetPeriod = null,
        [Description("Track plus ticks for Habit, or null.")] bool? TrackPlus = null,
        [Description("Track minus ticks for Habit, or null.")] bool? TrackMinus = null);

    [McpServerTool(Name = "batch_create_items", Title = "Batch create board items", OpenWorld = true)]
    [Description("Creates multiple habits, dailies, or todos in a single call. Returns array of created items.")]
    public async Task<IReadOnlyList<BoardItem>> BatchCreateItems(
        McpServer server,
        [Description("Array of items to create.")] IReadOnlyList<BatchCreateItemInput> items,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (items is null || items.Count == 0)
        {
            throw new McpProtocolException("items must not be empty.", McpErrorCode.InvalidParams);
        }

        var created = new List<BoardItem>(items.Count);
        foreach (var item in items)
        {
            var cleanTitle = McpInput.CleanTitle(item.Title);
            McpInput.CheckNotes(item.Notes);
            McpInput.CheckTags(item.Tags);
            var normalizedChecklist = item.ChecklistJson is not null ? McpInput.NormalizedChecklist(item.ChecklistJson) : null;
            DateOnly? parsedDate = null;
            if (item.Date is not null)
            {
                if (item.Section == BoardSection.Habit)
                {
                    throw new McpProtocolException("Habits take no date. Omit the date for Habit.", McpErrorCode.InvalidParams);
                }

                parsedDate = McpInput.ParseDate(item.Date);
            }

            int? weekdays = item.WeekdayNames is not null
                ? McpInput.ResolveWeekdaysWithNames(null, item.WeekdayNames, 0)
                : null;

            var res = await boards.CreateItemDetailedAsync(
                userId,
                item.Section,
                cleanTitle,
                item.Notes,
                item.Tags,
                parsedDate,
                null,
                normalizedChecklist,
                item.Repeat,
                item.RepeatInterval,
                weekdays,
                item.ResetPeriod,
                item.TrackPlus,
                item.TrackMinus,
                cancellationToken: cancellationToken);
            created.Add(res);
        }

        await McpNotifications.BoardChangedAsync(server, null, cancellationToken);
        return created;
    }

    [McpServerTool(Name = "rename_item", Title = "Rename board item", Idempotent = true, OpenWorld = true)]
    [Description("Renames a board item. Returns the updated item. Throws when not found.")]
    public async Task<BoardItem> RenameItem(
        McpServer server,
        [Description("Item id.")] Guid itemId,
        [Description("New title, 1 to 200 chars.")] string title,
        [Description("Section of the item. Optional; auto-detected if omitted.")] BoardSection? section = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var cleanTitle = McpInput.CleanTitle(title);
        var resolvedSection = section ?? await boards.GetItemSectionAsync(userId, itemId, cancellationToken)
            ?? throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams);

        var result = await boards.RenameItemAsync(userId, resolvedSection, itemId, cleanTitle, null, cancellationToken);
        var item = ThrowOnError(result);
        await McpNotifications.BoardChangedAsync(server, item.Id, cancellationToken);
        return item;
    }

    [McpServerTool(Name = "toggle_item", Title = "Toggle daily or todo", OpenWorld = true)]
    [Description("Toggles a daily or todo for today. You cannot uncomplete a past date. Completion there is append-only. For habits, use habit_plus and habit_minus instead. Returns the updated item.")]
    public async Task<BoardItem> ToggleItem(
        McpServer server,
        [Description("Item id.")] Guid itemId,
        [Description("Section: Daily or Todo. Optional; auto-detected if omitted.")] BoardSection? section = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var resolvedSection = section ?? await boards.GetItemSectionAsync(userId, itemId, cancellationToken)
            ?? throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams);

        if (resolvedSection == BoardSection.Habit)
        {
            throw new McpProtocolException("Habits cannot be toggled. Use habit_plus or habit_minus.", McpErrorCode.InvalidParams);
        }

        var result = await boards.ToggleItemAsync(userId, resolvedSection, itemId, null, cancellationToken);
        var item = ThrowOnError(result);
        await McpNotifications.BoardChangedAsync(server, item.Id, cancellationToken);
        return item;
    }

    [McpServerTool(Name = "batch_toggle_items", Title = "Batch toggle dailies or todos", OpenWorld = true)]
    [Description("Toggles multiple dailies or todos for today in one call. Habits cannot be toggled. Returns array of updated items.")]
    public async Task<IReadOnlyList<BoardItem>> BatchToggleItems(
        McpServer server,
        [Description("List of item GUIDs.")] IReadOnlyList<Guid> itemIds,
        [Description("Section: Daily or Todo. Optional; auto-detected per item if omitted.")] BoardSection? section = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (itemIds is null || itemIds.Count == 0)
        {
            throw new McpProtocolException("itemIds must not be empty.", McpErrorCode.InvalidParams);
        }

        var updated = new List<BoardItem>(itemIds.Count);
        foreach (var itemId in itemIds)
        {
            var s = section ?? await boards.GetItemSectionAsync(userId, itemId, cancellationToken)
                ?? throw new McpProtocolException($"Item not found: {itemId:D}", McpErrorCode.InvalidParams);

            if (s == BoardSection.Habit)
            {
                throw new McpProtocolException("Habits cannot be toggled. Use habit_plus or habit_minus.", McpErrorCode.InvalidParams);
            }

            var result = await boards.ToggleItemAsync(userId, s, itemId, null, cancellationToken);
            var item = ThrowOnError(result);
            updated.Add(item);
        }

        await McpNotifications.BoardChangedAsync(server, null, cancellationToken);
        return updated;
    }

    [McpServerTool(Name = "archive_item", Title = "Archive board item", Idempotent = true, OpenWorld = true)]
    [Description("Archives an item so it hides from the active board. Returns the updated item.")]
    public async Task<BoardItem> ArchiveItem(
        McpServer server,
        [Description("Item id.")] Guid itemId,
        [Description("Section of the item. Optional; auto-detected if omitted.")] BoardSection? section = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var resolvedSection = section ?? await boards.GetItemSectionAsync(userId, itemId, cancellationToken)
            ?? throw new McpProtocolException($"Item not found: {itemId:D}", McpErrorCode.InvalidParams);

        var result = await boards.ArchiveItemAsync(userId, resolvedSection, itemId, null, cancellationToken);
        var item = ThrowOnError(result);
        await McpNotifications.BoardChangedAsync(server, item.Id, cancellationToken);
        return item;
    }

    [McpServerTool(Name = "bulk_archive", Title = "Bulk archive items", Idempotent = true, OpenWorld = true)]
    [Description("Archives multiple items so they hide from the active board. Returns array of updated items. Archiving more than 5 items requests confirmation.")]
    public async Task<IReadOnlyList<BoardItem>> BulkArchive(
        McpServer server,
        RequestContext<CallToolRequestParams> ctx,
        [Description("List of item GUIDs.")] IReadOnlyList<Guid> itemIds,
        [Description("Section of the items. Optional; auto-detected per item if omitted.")] BoardSection? section = null,
        [Description("Confirmation token. Pass CONFIRM to confirm bulk archive of >5 items.")] string? confirmToken = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (itemIds is null || itemIds.Count == 0)
        {
            throw new McpProtocolException("itemIds must not be empty.", McpErrorCode.InvalidParams);
        }

        if (itemIds.Count > 5 && !string.Equals(confirmToken, "CONFIRM", StringComparison.Ordinal) && !ConfirmBulkArchiveAccepted(ctx))
        {
            if (server.IsMrtrSupported)
            {
                throw NeedBulkArchiveConfirm(itemIds.Count);
            }

            throw new McpProtocolException($"Archiving {itemIds.Count} items needs confirmation. Retry with confirmToken set to 'CONFIRM'.", McpErrorCode.InvalidParams);
        }

        try
        {
            var updated = await boards.BulkArchiveAsync(userId, itemIds, cancellationToken);
            await McpNotifications.BoardChangedAsync(server, null, cancellationToken);
            return updated;
        }
        catch (KeyNotFoundException ex)
        {
            throw new McpProtocolException(ex.Message, McpErrorCode.InvalidParams);
        }
    }

    [McpServerTool(Name = "unarchive_item", Title = "Unarchive board item", Idempotent = true, OpenWorld = true)]
    [Description("Restores an archived item to the active board. Returns the updated item.")]
    public async Task<BoardItem> UnarchiveItem(
        McpServer server,
        [Description("Item id.")] Guid itemId,
        [Description("Section of the item. Optional; auto-detected if omitted.")] BoardSection? section = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var resolvedSection = section ?? await boards.GetItemSectionAsync(userId, itemId, cancellationToken)
            ?? throw new McpProtocolException($"Item not found: {itemId:D}", McpErrorCode.InvalidParams);

        var result = await boards.UnarchiveItemAsync(userId, resolvedSection, itemId, null, cancellationToken);
        var item = ThrowOnError(result);
        await McpNotifications.BoardChangedAsync(server, item.Id, cancellationToken);
        return item;
    }

    [McpServerTool(Name = "bulk_unarchive", Title = "Bulk unarchive items", Idempotent = true, OpenWorld = true)]
    [Description("Restores multiple archived items to the active board. Returns array of updated items.")]
    public async Task<IReadOnlyList<BoardItem>> BulkUnarchive(
        McpServer server,
        [Description("List of item GUIDs.")] IReadOnlyList<Guid> itemIds,
        [Description("Section of the items. Optional; auto-detected per item if omitted.")] BoardSection? section = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (itemIds is null || itemIds.Count == 0)
        {
            throw new McpProtocolException("itemIds must not be empty.", McpErrorCode.InvalidParams);
        }

        try
        {
            var updated = await boards.BulkUnarchiveAsync(userId, itemIds, cancellationToken);
            await McpNotifications.BoardChangedAsync(server, null, cancellationToken);
            return updated;
        }
        catch (KeyNotFoundException ex)
        {
            throw new McpProtocolException(ex.Message, McpErrorCode.InvalidParams);
        }
    }

    [McpServerTool(Name = "rename_tag", Title = "Rename tag across items", Idempotent = true, OpenWorld = true)]
    [Description("Renames a tag across all items for the current user. Returns status message.")]
    public async Task<string> RenameTag(
        McpServer server,
        [Description("Current tag name to rename.")] string oldTag,
        [Description("New tag name.")] string newTag,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (string.IsNullOrWhiteSpace(oldTag) || string.IsNullOrWhiteSpace(newTag))
        {
            throw new McpProtocolException("oldTag and newTag must not be empty.", McpErrorCode.InvalidParams);
        }

        var count = await boards.RenameTagAsync(userId, oldTag, newTag, cancellationToken);
        await McpNotifications.BoardChangedAsync(server, null, cancellationToken);
        return $"Renamed tag '{oldTag}' to '{newTag}' on {count} items.";
    }

    [McpServerTool(Name = "delete_tag", Title = "Delete tag across items", Destructive = true, OpenWorld = true)]
    [Description("Removes a tag from all items for the current user. Returns status message.")]
    public async Task<string> DeleteTag(
        McpServer server,
        [Description("Tag name to remove from all items.")] string tag,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new McpProtocolException("tag must not be empty.", McpErrorCode.InvalidParams);
        }

        var count = await boards.DeleteTagAsync(userId, tag, cancellationToken);
        await McpNotifications.BoardChangedAsync(server, null, cancellationToken);
        return $"Removed tag '{tag}' from {count} items.";
    }

    [McpServerTool(Name = "delete_item", Title = "Delete board item", Destructive = true, OpenWorld = true)]
    [Description("Soft deletes an item. The row hides from the board. Creating the item again with the same id restores it. Ask the user for confirmation first, then retry with confirmToken set to the item id string.")]
    public async Task<string> DeleteItem(
        McpServer server,
        RequestContext<CallToolRequestParams> ctx,
        [Description("Item id.")] Guid itemId,
        [Description("Section of the item. Optional; auto-detected if omitted.")] BoardSection? section = null,
        [Description("Confirmation token. Pass the item id string to confirm. Omit on the first call.")]
        string? confirmToken = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var resolvedSection = section ?? await boards.GetItemSectionAsync(userId, itemId, cancellationToken)
            ?? throw new McpProtocolException($"Item not found: {itemId:D}", McpErrorCode.InvalidParams);

        if (!string.Equals(confirmToken, itemId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            && !ConfirmDeleteAccepted(ctx))
        {
            if (server.IsMrtrSupported)
            {
                throw NeedDeleteConfirm(resolvedSection, itemId);
            }

            throw new McpProtocolException(
                $"Delete needs confirmation. Retry with confirmToken set to '{itemId:D}'.",
                McpErrorCode.InvalidParams);
        }

        var result = await boards.DeleteItemAsync(userId, resolvedSection, itemId, null, cancellationToken);
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

    private static bool ConfirmBulkArchiveAccepted(RequestContext<CallToolRequestParams> ctx)
    {
        if (ctx.Params?.InputResponses is null
            || !ctx.Params.InputResponses.TryGetValue("confirm_bulk_archive", out var resp)
            || resp is null)
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

    private static InputRequiredException NeedBulkArchiveConfirm(int count) =>
        new(new Dictionary<string, InputRequest>
        {
            ["confirm_bulk_archive"] = InputRequest.ForElicitation(new ElicitRequestParams
            {
                Message = $"Archive {count} items? They will hide from the active board.",
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        ["confirm"] = new ElicitRequestParams.BooleanSchema
                        {
                            Description = "Confirm bulk archive",
                            Default = false,
                        },
                    },
                    Required = ["confirm"],
                },
            }),
        }, requestState: JsonSerializer.Serialize(new { tool = "bulk_archive", count }));
}
