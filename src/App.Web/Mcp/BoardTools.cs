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
    BoardIdempotencyService idem,
    IHttpContextAccessor http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "get_board_summary", Title = "Get board summary", ReadOnly = true, OpenWorld = false)]
    [Description("Returns a summary of active habits, completed and pending dailies, active and overdue todos, and streaks. Output format is markdown or json. Optional tag filters to one exact tag (case-insensitive).")]
    public async Task<string> GetBoardSummary(
        [Description("Output format: markdown or json. Defaults to markdown.")] string? format = "markdown",
        [Description("Exact tag to filter by (case-insensitive), or null for all. Max 500 chars.")] string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (tag is not null && tag.Length > McpInput.MaxTagsLength)
        {
            throw new McpFieldException("tag",
                $"expected {McpInput.MaxTagsLength} chars or fewer, got {tag.Length} chars.");
        }

        var snapshot = await boards.GetSnapshotAsync(userId, 200, cancellationToken);
        var today = await boards.TodayAsync(userId, cancellationToken);

        var habits = snapshot.Habits;
        var dailies = snapshot.Dailies;
        var todos = snapshot.Todos;
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var wanted = tag.Trim();
            habits = [.. habits.Where(h => BoardTagUtil.ParseTags(h.Tags).Any(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase)))];
            dailies = [.. dailies.Where(d => BoardTagUtil.ParseTags(d.Tags).Any(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase)))];
            todos = [.. todos.Where(t => BoardTagUtil.ParseTags(t.Tags).Any(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase)))];
        }

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            var summary = new
            {
                HabitsCount = habits.Count,
                TotalHabitTicks = habits.Sum(h => h.Counter),
                DailiesCount = dailies.Count,
                DailiesCompletedToday = dailies.Count(d => d.IsCompleted),
                DailiesPendingToday = dailies.Count(d => !d.IsCompleted),
                TodosActiveCount = todos.Count(t => !t.IsCompleted),
                TodosCompletedCount = todos.Count(t => t.IsCompleted),
                TodosOverdueCount = todos.Count(t => !t.IsCompleted && t.TodoDueDate.HasValue && t.TodoDueDate.Value < today),
                TopStreaks = dailies
                    .Where(d => d.Counter > 0)
                    .OrderByDescending(d => d.Counter)
                    .Take(5)
                    .Select(d => new { d.Title, Streak = d.Counter })
                    .ToList()
            };
            return JsonSerializer.Serialize(summary, Json);
        }

        var pendingDailies = dailies.Where(d => !d.IsCompleted).ToList();
        var doneDailies = dailies.Where(d => d.IsCompleted).ToList();
        var overdueTodos = todos.Where(t => !t.IsCompleted && t.TodoDueDate.HasValue && t.TodoDueDate.Value < today).ToList();
        var activeTodos = todos.Where(t => !t.IsCompleted && (!t.TodoDueDate.HasValue || t.TodoDueDate.Value >= today)).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Board Summary ({today:yyyy-MM-dd})");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Dailies:** {doneDailies.Count}/{dailies.Count} done");
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
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Habits:** {habits.Count} tracked");
        foreach (var h in habits.Take(5))
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
    [Description("Reads one active board item by id or title. Throws when not found.")]
    public async Task<BoardItem> GetItem(
        [Description("Board item id as GUID string. Optional if title is provided.")]
        Guid? itemId = null,
        [Description("Item title to match if itemId is omitted.")]
        string? title = null,
        [Description("Section: Habit, Daily, or Todo. Optional and auto-detected when omitted.")]
        BoardSection? section = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var resolvedId = await McpItemResolver.ResolveItemIdAsync(boards, userId, itemId, title, section, cancellationToken: cancellationToken);
        return await boards.GetItemAsync(userId, resolvedId, cancellationToken)
            ?? throw new McpProtocolException($"Item not found: {resolvedId:D}", McpErrorCode.InvalidParams);
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
            throw new McpFieldException("cursor",
                "expected the NextCursor of a prior call, got empty.");
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
            BoardMutationStatus.NotFound => throw new McpFieldException("itemId",
                $"no item found with id {itemId:D}. Check it with get_snapshot or search_items."),
            BoardMutationStatus.Conflict => throw new McpProtocolException(
                "Version conflict. Read the item again and retry.", McpErrorCode.InvalidRequest),
            _ => throw new McpFieldException("itemId",
                $"no item found with id {itemId:D}. Check it with get_snapshot or search_items."),
        };
        await McpNotifications.BoardChangedAsync(server, item.Id, cancellationToken);
        return item;
    }

    [McpServerTool(Name = "create_item", Title = "Create board item", OpenWorld = true)]
    [Description("Creates a habit, daily, or todo with title, schedule, tracking, and optional checklist. Warns on near-duplicate titles unless force or allowDuplicate is true.")]
    public async Task<BoardItem> CreateItem(
        McpServer server,
        [Description("Section: Habit, Daily, or Todo.")]
        BoardSection section,
        [Description("Title, 1 to 200 chars.")]
        string title,
        [Description("Notes up to 4000 chars, or null.")] string? notes = null,
        [Description("Tags as comma-separated string or array of strings, up to 500 chars, or null. Example: \"work,urgent\" or [\"work\",\"urgent\"].")] string? tags = null,
        [Description("Date as yyyy-MM-dd. For Daily it sets the start date. For Todo it sets the due date. Omit the date for Habit.")]
        string? date = null,
        [Description("Checklist as JSON string or native array of {text, isDone} objects, 8000 chars or fewer, or null.")]
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
        [Description("Optional idempotency key (max 128 chars). Reusing a key with identical fields returns the original item instead of creating a duplicate.")]
        string? idempotencyKey = null,
        [Description("When true, validates all fields and returns the would-be item with a fresh id without persisting anything.")]
        bool dryRun = false,
        [Description("Set true to create even when a similarly titled active item exists.")]
        bool allowDuplicate = false,
        [Description("Alias for allowDuplicate. Set true to create even when a similarly titled active item exists.")]
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var validated = ValidateCreateFields(
            section, title, notes, tags, date, checklistJson, repeat, repeatInterval,
            weekdays, weekdayNames, resetPeriod, trackPlus, trackMinus, counter, negativeCounter);

        if (dryRun)
        {
            await ThrowOnDuplicateAsync(userId, validated.Section, validated.Title, allowDuplicate || force, cancellationToken);
            return PreviewItem(validated);
        }

        // The duplicate check runs inside the idempotent unit, so a retry
        // with the same key replays the original item.
        var allow = allowDuplicate || force;
        return await McpIdempotency.ExecuteAsync(
            idem,
            userId,
            idempotencyKey,
            "create_item",
            FingerprintOf(validated),
            async ct =>
            {
                await ThrowOnDuplicateAsync(userId, validated.Section, validated.Title, allow, ct);
                return await PersistValidatedAsync(server, userId, validated, ct);
            },
            cancellationToken);
    }

    public sealed record BatchCreateItemInput(
        [Description("Section: Habit, Daily, or Todo.")] BoardSection Section,
        [Description("Title, 1 to 200 chars.")] string Title,
        [Description("Notes up to 4000 chars, or null.")] string? Notes = null,
        [Description("Tags as comma-separated string or array of strings, up to 500 chars, or null.")] string? Tags = null,
        [Description("Date as yyyy-MM-dd. Start date for Daily, due date for Todo. Omit for Habit.")] string? Date = null,
        [Description("Checklist as JSON string or native array of {text, isDone}, or null.")] string? ChecklistJson = null,
        [Description("Repeat schedule for Daily: Daily, Weekly, Monthly, or Yearly, or null.")] DailyRepeatType? Repeat = null,
        [Description("Repeat interval for Daily, 1 to 999, or null.")] int? RepeatInterval = null,
        [Description("Weekday names like [\"Mon\", \"Wed\", \"Fri\"] for weekly Daily, or null.")] IReadOnlyList<string>? WeekdayNames = null,
        [Description("Habit reset period: Daily, Weekly, or Monthly, or null.")] HabitResetPeriod? ResetPeriod = null,
        [Description("Track plus ticks for Habit, or null.")] bool? TrackPlus = null,
        [Description("Track minus ticks for Habit, or null.")] bool? TrackMinus = null,
        [Description("Optional per-item idempotency key (max 128 chars). Falls back to the batch key with the item index.")]
        string? IdempotencyKey = null,
        [Description("Set true to create this item even when a similarly titled active item exists.")]
        bool AllowDuplicate = false,
        [Description("Alias for AllowDuplicate. Set true to create even when a similarly titled active item exists.")]
        bool Force = false);

    /// <summary>
    /// Per-item outcome of <c>batch_create_items</c>.
    /// Failed items carry <c>Field</c>, <c>Error</c>, and <c>Message</c>.
    /// </summary>
    public sealed record BatchCreateItemResult(
        int Index,
        bool Ok,
        Guid? Id,
        BoardItem? Item,
        string? Field,
        string? Error,
        string? Message,
        bool DryRun);

    [McpServerTool(Name = "batch_create_items", Title = "Batch create board items", OpenWorld = true)]
    [Description("Creates multiple habits, dailies, or todos in one call. Returns one per-item result per input: ok results carry the created id, failed results carry field, error, and message. Valid items persist even when siblings fail.")]
    public async Task<IReadOnlyList<BatchCreateItemResult>> BatchCreateItems(
        McpServer server,
        [Description("Array of items to create.")] IReadOnlyList<BatchCreateItemInput> items,
        [Description("Optional batch idempotency key (max 128 chars). Each item reuses it with its index, so reposting the same batch with the same key creates items exactly once.")]
        string? idempotencyKey = null,
        [Description("When true, validates every item and returns the would-be results with preview ids without persisting anything.")]
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (items is null || items.Count == 0)
        {
            throw new McpProtocolException("items: expected a non-empty array, got empty.", McpErrorCode.InvalidParams);
        }

        McpInput.CheckIdempotencyKey(idempotencyKey);

        var results = new List<BatchCreateItemResult>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            string? itemKey = items[i].IdempotencyKey;
            if (string.IsNullOrWhiteSpace(itemKey) && idempotencyKey is not null)
            {
                itemKey = $"{idempotencyKey}#{i}";
            }

            results.Add(await ProcessBatchItemAsync(server, userId, items[i], i, itemKey, dryRun, cancellationToken));
        }

        if (!dryRun)
        {
            await McpNotifications.BoardChangedAsync(server, null, cancellationToken);
        }

        return results;
    }

    private async Task<BatchCreateItemResult> ProcessBatchItemAsync(
        McpServer server,
        Guid userId,
        BatchCreateItemInput item,
        int index,
        string? idempotencyKey,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        try
        {
            var validated = ValidateCreateFields(
                item.Section, item.Title, item.Notes, item.Tags, item.Date, item.ChecklistJson,
                item.Repeat, item.RepeatInterval, null, item.WeekdayNames, item.ResetPeriod,
                item.TrackPlus, item.TrackMinus, null, null, "checklistJson");

            if (dryRun)
            {
                await ThrowOnDuplicateAsync(userId, validated.Section, validated.Title, item.AllowDuplicate || item.Force, cancellationToken);
                var preview = PreviewItem(validated);
                return new BatchCreateItemResult(index, true, preview.Id, preview, null, null, "Dry run: validated, nothing persisted.", true);
            }

            // The duplicate check runs inside the idempotent unit, so a retried
            // batch replays its original items.
            var batchAllow = item.AllowDuplicate || item.Force;
            var created = await McpIdempotency.ExecuteAsync(
                idem,
                userId,
                idempotencyKey,
                "batch_create_items",
                FingerprintOf(validated),
                async ct =>
                {
                    await ThrowOnDuplicateAsync(userId, validated.Section, validated.Title, batchAllow, ct);
                    return await PersistValidatedAsync(server, userId, validated, ct);
                },
                cancellationToken);
            return new BatchCreateItemResult(index, true, created.Id, created, null, null, "Created.", false);
        }
        catch (DuplicateTitleException ex)
        {
            return new BatchCreateItemResult(index, false, null, null, ex.Field, "duplicate", ex.Message, dryRun);
        }
        catch (McpFieldException ex)
        {
            return new BatchCreateItemResult(index, false, null, null, ex.Field, "validation", ex.Message, dryRun);
        }
        catch (McpProtocolException ex)
        {
            var error = ex.ErrorCode == McpErrorCode.InvalidParams ? "validation" : ex.ErrorCode.ToString();
            if (ex.Message.Contains("idempotencyKey", StringComparison.Ordinal))
            {
                error = "idempotency";
            }

            return new BatchCreateItemResult(index, false, null, null, null, error, ex.Message, dryRun);
        }
    }

    private sealed record ValidatedCreate(
        BoardSection Section,
        string Title,
        string? Notes,
        string? Tags,
        DateOnly? Date,
        string? ChecklistJson,
        DailyRepeatType? Repeat,
        int? RepeatInterval,
        int? Weekdays,
        HabitResetPeriod? ResetPeriod,
        bool? TrackPlus,
        bool? TrackMinus,
        int? Counter,
        int? NegativeCounter);

    private static ValidatedCreate ValidateCreateFields(
        BoardSection section,
        string title,
        string? notes,
        string? tags,
        string? date,
        string? checklistJson,
        DailyRepeatType? repeat,
        int? repeatInterval,
        int? weekdays,
        IReadOnlyList<string>? weekdayNames,
        HabitResetPeriod? resetPeriod,
        bool? trackPlus,
        bool? trackMinus,
        int? counter,
        int? negativeCounter,
        string checklistField = "checklistJson")
    {
        var cleanTitle = McpInput.CleanTitle(title);
        McpInput.CheckNotes(notes);
        var canonicalTags = McpInput.CanonicalTags(tags);
        McpInput.CheckTags(canonicalTags);
        var normalizedChecklist = checklistJson is not null ? McpInput.NormalizedChecklist(checklistJson, checklistField) : null;

        DateOnly? parsedDate = null;
        if (date is not null)
        {
            if (section == BoardSection.Habit)
            {
                throw new McpFieldException("date", "expected no date for Habit, got one. Omit the date for Habit.");
            }

            parsedDate = McpInput.ParseDate(date, nameof(date));
        }

        return new ValidatedCreate(
            section,
            cleanTitle,
            notes,
            canonicalTags,
            parsedDate,
            normalizedChecklist,
            repeat,
            repeatInterval.HasValue ? McpInput.ResolveRepeatInterval(repeatInterval, 1) : null,
            (weekdays.HasValue || weekdayNames is { Count: > 0 })
                ? McpInput.ResolveWeekdaysWithNames(weekdays, weekdayNames, 0)
                : null,
            resetPeriod,
            trackPlus,
            trackMinus,
            counter.HasValue ? McpInput.ResolveDailyCounter(counter, 0) : null,
            negativeCounter.HasValue ? McpInput.ResolveHabitCounter(negativeCounter, 0, "Negative counter") : null);
    }

    private static object FingerprintOf(ValidatedCreate validated) => new
    {
        section = validated.Section.ToString(),
        validated.Title,
        validated.Notes,
        validated.Tags,
        date = validated.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        validated.ChecklistJson,
        repeat = validated.Repeat?.ToString(),
        validated.RepeatInterval,
        validated.Weekdays,
        resetPeriod = validated.ResetPeriod?.ToString(),
        validated.TrackPlus,
        validated.TrackMinus,
        validated.Counter,
        validated.NegativeCounter,
    };

    private async Task ThrowOnDuplicateAsync(
        Guid userId,
        BoardSection section,
        string title,
        bool allowDuplicate,
        CancellationToken cancellationToken)
    {
        if (allowDuplicate)
        {
            return;
        }

        var similar = await boards.FindSimilarActiveTitlesAsync(userId, section, title, 1, cancellationToken);
        if (similar.Count > 0)
        {
            throw new DuplicateTitleException(similar[0].Id, similar[0].Title);
        }
    }

    private static BoardItem PreviewItem(ValidatedCreate validated) =>
        new(
            Guid.NewGuid(),
            validated.Title,
            false,
            validated.Counter ?? 0,
            validated.Notes,
            validated.Tags,
            validated.TrackPlus ?? true,
            validated.TrackMinus ?? true,
            validated.NegativeCounter ?? 0,
            validated.ResetPeriod ?? HabitResetPeriod.Daily,
            validated.Section == BoardSection.Daily ? validated.Date : null,
            validated.Repeat ?? DailyRepeatType.Daily,
            validated.RepeatInterval ?? 1,
            validated.ChecklistJson,
            null,
            validated.Section == BoardSection.Todo ? validated.Date : null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            false,
            null,
            validated.Weekdays ?? 0);

    private async Task<BoardItem> PersistValidatedAsync(
        McpServer server,
        Guid userId,
        ValidatedCreate validated,
        CancellationToken cancellationToken)
    {
        var created = await boards.CreateItemDetailedAsync(
            userId,
            validated.Section,
            validated.Title,
            validated.Notes,
            validated.Tags,
            validated.Date,
            null,
            validated.ChecklistJson,
            validated.Repeat,
            validated.RepeatInterval,
            validated.Weekdays,
            validated.ResetPeriod,
            validated.TrackPlus,
            validated.TrackMinus,
            validated.Counter,
            validated.NegativeCounter,
            cancellationToken);
        await McpNotifications.BoardChangedAsync(server, created.Id, cancellationToken);
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
            ?? throw new McpFieldException("itemId",
                $"no item found with id {itemId:D}. Check it with get_snapshot or search_items.");

        var result = await boards.RenameItemAsync(userId, resolvedSection, itemId, cleanTitle, null, cancellationToken);
        var item = ThrowOnError(result, itemId);
        await McpNotifications.BoardChangedAsync(server, item.Id, cancellationToken);
        return item;
    }

    [McpServerTool(Name = "toggle_item", Title = "Toggle daily or todo", OpenWorld = true)]
    [Description("Toggles a daily or todo for today. Resolves by itemId or title. You cannot uncomplete a past date. Completion there is append-only. For habits, use habit_plus and habit_minus instead. Returns the updated item.")]
    public async Task<BoardItem> ToggleItem(
        McpServer server,
        [Description("Item id. Optional if title is provided.")] Guid? itemId = null,
        [Description("Item title to match if itemId is omitted.")] string? title = null,
        [Description("Section: Daily or Todo. Optional; auto-detected if omitted.")] BoardSection? section = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var resolvedId = await McpItemResolver.ResolveItemIdAsync(boards, userId, itemId, title, section, cancellationToken: cancellationToken);
        var resolvedSection = section ?? await boards.GetItemSectionAsync(userId, resolvedId, cancellationToken)
            ?? throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams);

        if (resolvedSection == BoardSection.Habit)
        {
            throw new McpProtocolException("Habits cannot be toggled. Use habit_plus or habit_minus.", McpErrorCode.InvalidParams);
        }

        var result = await boards.ToggleItemAsync(userId, resolvedSection, resolvedId, null, cancellationToken);
        var item = ThrowOnError(result, resolvedId);
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
            throw new McpFieldException("itemIds", "expected a non-empty array, got empty.");
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
            var item = ThrowOnError(result, itemId);
            updated.Add(item);
        }

        await McpNotifications.BoardChangedAsync(server, null, cancellationToken);
        return updated;
    }

    [McpServerTool(Name = "archive_item", Title = "Archive board item", Idempotent = true, OpenWorld = true)]
    [Description("Archives an item so it hides from the active board. Resolves by itemId or title. Returns the updated item.")]
    public async Task<BoardItem> ArchiveItem(
        McpServer server,
        [Description("Item id. Optional if title is provided.")] Guid? itemId = null,
        [Description("Item title to match if itemId is omitted.")] string? title = null,
        [Description("Section of the item. Optional; auto-detected if omitted.")] BoardSection? section = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var resolvedId = await McpItemResolver.ResolveItemIdAsync(boards, userId, itemId, title, section, cancellationToken: cancellationToken);
        var resolvedSection = section ?? await boards.GetItemSectionAsync(userId, resolvedId, cancellationToken)
            ?? throw new McpProtocolException($"Item not found: {resolvedId:D}", McpErrorCode.InvalidParams);

        var result = await boards.ArchiveItemAsync(userId, resolvedSection, resolvedId, null, cancellationToken);
        var item = ThrowOnError(result, resolvedId);
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
            throw new McpFieldException("itemIds", "expected a non-empty array, got empty.");
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
    [Description("Restores an archived item to the active board. Resolves by itemId or title. Returns the updated item.")]
    public async Task<BoardItem> UnarchiveItem(
        McpServer server,
        [Description("Item id. Optional if title is provided.")] Guid? itemId = null,
        [Description("Item title to match if itemId is omitted.")] string? title = null,
        [Description("Section of the item. Optional; auto-detected if omitted.")] BoardSection? section = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var resolvedId = await McpItemResolver.ResolveItemIdAsync(boards, userId, itemId, title, section, includeArchived: true, cancellationToken: cancellationToken);
        var resolvedSection = section ?? await boards.GetItemSectionAsync(userId, resolvedId, cancellationToken)
            ?? throw new McpProtocolException($"Item not found: {resolvedId:D}", McpErrorCode.InvalidParams);

        var result = await boards.UnarchiveItemAsync(userId, resolvedSection, resolvedId, null, cancellationToken);
        var item = ThrowOnError(result, resolvedId);
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
            throw new McpFieldException("itemIds", "expected a non-empty array, got empty.");
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
        if (string.IsNullOrWhiteSpace(oldTag))
        {
            throw new McpFieldException("oldTag",
                $"expected a non-empty tag name, got {McpInput.PreviewReceived(oldTag)}.");
        }

        if (string.IsNullOrWhiteSpace(newTag))
        {
            throw new McpFieldException("newTag",
                $"expected a non-empty tag name, got {McpInput.PreviewReceived(newTag)}.");
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
            throw new McpFieldException("tag",
                $"expected a non-empty tag name, got {McpInput.PreviewReceived(tag)}.");
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

    private static BoardItem ThrowOnError(BoardMutationResult result, Guid itemId) =>
        result.Status switch
        {
            BoardMutationStatus.Ok when result.Item is not null => result.Item,
            BoardMutationStatus.NotFound => throw new McpFieldException("itemId",
                $"no item found with id {itemId:D}. Check it with get_snapshot or search_items."),
            BoardMutationStatus.Conflict => throw new McpProtocolException(
                "Version conflict. Read the item again and retry.", McpErrorCode.InvalidRequest),
            _ => throw new McpFieldException("itemId",
                $"no item found with id {itemId:D}. Check it with get_snapshot or search_items."),
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
                Message = $"Delete {section} '{itemId:D}'? This hides it from the board. " +
                    $"Headless clients without elicitation: retry with confirmToken set to '{itemId:D}'.",
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
                Message = $"Archive {count} items? They will hide from the active board. " +
                    "Headless clients without elicitation: retry with confirmToken set to 'CONFIRM'.",
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
