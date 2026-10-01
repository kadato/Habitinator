using App.Shared.RCL.Models;

using Microsoft.Extensions.DependencyInjection;

namespace App.Shared.RCL.Services.Board.Local;

public sealed partial class LocalFirstBoardDataService
{
    public Task<BoardItem> CreateItemAsync(BoardSection section, string title, Guid? itemId = null,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (local, userKey) =>
            {
                var id = itemId ?? Guid.NewGuid();
                var now = DateTimeOffset.UtcNow;
                var sortOrder = await GetInitialLocalSortOrderAsync(local, userKey, section, cancellationToken);
                var today = await TodayAsync(cancellationToken);
                BoardItem item = section == BoardSection.Habit
                    ? new(id, ZalgoSanitizer.SanitizeAndTrim(title), CreatedAtUtc: now, SortOrder: sortOrder,
                        HabitPeriodStart: HabitResetSchedule.PeriodStartFor(today, HabitResetPeriod.Daily))
                    : new(id, ZalgoSanitizer.SanitizeAndTrim(title), CreatedAtUtc: now, SortOrder: sortOrder);
                await local.UpsertItemAsync(BoardLocalRow.FromModel(section, userKey, item, true), cancellationToken);
                CreateOutboxPayload payload = new(section, item.Title, id);
                await EnqueueStaticAsync(local, userKey, BoardOutboxOperationKind.Create, payload, cancellationToken);
                return item;
            },
            cancellationToken);

    public Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (local, userKey) => await UpdateRowAsync(
                local,
                userKey,
                new RowUpdateOp(itemId, null, BoardOutboxOperationKind.Rename, (row, expected) => new RenameOutboxPayload(section, itemId, row.Title, expected)),
                row =>
                {
                    row.Title = ZalgoSanitizer.SanitizeAndTrim(title);
                    return Task.CompletedTask;
                },
                cancellationToken),
            cancellationToken);

    public Task<bool> DeleteItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (local, userKey) =>
            {
                if (await TryCoalesceDeletePendingCreateAsync(local, userKey, itemId, cancellationToken))
                {
                    return true;
                }

                return await UpdateRowAsync(
                    local,
                    userKey,
                    new RowUpdateOp(itemId, null, BoardOutboxOperationKind.Delete, (row, expected) => new SectionItemOutboxPayload(section, itemId, expected)),
                    row =>
                    {
                        row.IsArchived = true;
                        return Task.CompletedTask;
                    },
                    cancellationToken,
                    deleteAfterSave: true) is not null;
            },
            cancellationToken);

    public Task<BoardItem?> ArchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (local, userKey) => await UpdateRowAsync(
                local,
                userKey,
                new RowUpdateOp(itemId, null, BoardOutboxOperationKind.Archive, (row, expected) => new SectionItemOutboxPayload(section, itemId, expected)),
                row =>
                {
                    row.IsArchived = true;
                    row.ServerUpdatedAtUtc = DateTimeOffset.UtcNow;
                    return Task.CompletedTask;
                },
                cancellationToken),
            cancellationToken);

    public Task<BoardItem?> UnarchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (local, userKey) => await UpdateRowAsync(
                local,
                userKey,
                new RowUpdateOp(itemId, null, BoardOutboxOperationKind.Unarchive, (row, expected) => new SectionItemOutboxPayload(section, itemId, expected)),
                row =>
                {
                    row.IsArchived = false;
                    row.ServerUpdatedAtUtc = DateTimeOffset.UtcNow;
                    return Task.CompletedTask;
                },
                cancellationToken),
            cancellationToken);

    public async Task<BoardSnapshot> GetArchivedSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await store.EnsureReadyAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userKey = await users.GetUserKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(userKey))
            {
                return EmptySnapshot();
            }

            var today = await TodayAsync(cancellationToken);
            var items = (await store.ListItemsAsync(userKey, includeArchived: true, cancellationToken))
                .Where(x => x.IsArchived)
                .ToList();

            var (habits, dailies, todos) = OrderRows(items, today);
            return new(habits, dailies, todos);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (local, userKey) =>
            {
                var today = await TodayAsync(cancellationToken);
                var result = await UpdateRowAsync(
                    local,
                    userKey,
                    new RowUpdateOp(itemId, null, BoardOutboxOperationKind.Toggle, (row, expected) => new SectionItemOutboxPayload(section, itemId, expected)),
                    row =>
                    {
                        ApplyLocalToggle(section, row, today);
                        return Task.CompletedTask;
                    },
                    cancellationToken);

                if (result != null && ResolveToggleActivityEvent(section, result, today) is { } evt)
                {
                    await AppendLocalActivityAsync(evt, itemId, result.Title, cancellationToken);
                }

                return result;
            },
            cancellationToken);

    private static ActivityEventType? ResolveToggleActivityEvent(BoardSection section, BoardItem result, DateOnly today) =>
        section switch
        {
            BoardSection.Daily => result.DailyLastCompletedOn == today ? ActivityEventType.DailyComplete : ActivityEventType.DailyUncomplete,
            BoardSection.Todo => result.IsCompleted ? ActivityEventType.TodoComplete : ActivityEventType.TodoUncomplete,
            _ => null
        };

    public async Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn,
        CancellationToken cancellationToken = default)
    {
        var today = await TodayAsync(cancellationToken);
        return await MutateWithSyncAsync(
            async (local, userKey) =>
            {
                var result = await UpdateRowAsync(
                    local,
                    userKey,
                    new RowUpdateOp(itemId, BoardSection.Daily, BoardOutboxOperationKind.CompleteDailyForDate, (row, expected) => new CompleteDailyOutboxPayload(itemId, completedOn, expected)),
                    row =>
                    {
                        row.DailyLastCompletedOn = completedOn;
                        row.IsCompleted = false;
                        row.Counter = Math.Max(1, row.Counter + 1);
                        return Task.CompletedTask;
                    },
                    cancellationToken,
                    row => CanCompleteDailyForDate(row, completedOn, today));

                if (result != null)
                {
                    await AppendLocalActivityAsync(ActivityEventType.DailyComplete, itemId, result.Title, cancellationToken);
                }

                return result;
            },
            cancellationToken);
    }

    private static bool CanCompleteDailyForDate(BoardLocalRow row, DateOnly completedOn, DateOnly today) =>
        DailySchedule.CanCompleteForDate(
            row.DailyStartDate, row.DailyRepeat, row.DailyRepeatInterval, row.DailyLastCompletedOn, completedOn, today);

    public Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (local, userKey) =>
            {
                var today = await TodayAsync(cancellationToken);
                var result = await UpdateRowAsync(
                    local,
                    userKey,
                    new RowUpdateOp(itemId, BoardSection.Habit, BoardOutboxOperationKind.HabitIncrement, (row, expected) => new ItemIdOutboxPayload(itemId, expected)),
                    row =>
                    {
                        EnsureLocalHabitPeriodCurrent(row, today);
                        if (row.TrackPlus)
                        {
                            row.Counter++;
                        }

                        return Task.CompletedTask;
                    },
                    cancellationToken);

                if (result != null && result.TrackPlus)
                {
                    await AppendLocalActivityAsync(ActivityEventType.HabitPlus, itemId, result.Title, cancellationToken);
                }

                return result;
            },
            cancellationToken);

    public Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (local, userKey) =>
            {
                var today = await TodayAsync(cancellationToken);
                var result = await UpdateRowAsync(
                    local,
                    userKey,
                    new RowUpdateOp(itemId, BoardSection.Habit, BoardOutboxOperationKind.HabitDecrement, (row, expected) => new ItemIdOutboxPayload(itemId, expected)),
                    row =>
                    {
                        EnsureLocalHabitPeriodCurrent(row, today);
                        if (row.TrackMinus)
                        {
                            row.NegativeCounter++;
                        }

                        return Task.CompletedTask;
                    },
                    cancellationToken);

                if (result != null && result.TrackMinus)
                {
                    await AppendLocalActivityAsync(ActivityEventType.HabitMinus, itemId, result.Title, cancellationToken);
                }

                return result;
            },
            cancellationToken);

    private readonly record struct RowUpdateOp(
        Guid ItemId,
        BoardSection? Section,
        BoardOutboxOperationKind Kind,
        Func<BoardLocalRow, DateTimeOffset?, object> PayloadFactory);

    private static async Task<BoardItem?> UpdateRowAsync(
        IBoardLocalStore local,
        string userKey,
        RowUpdateOp op,
        Func<BoardLocalRow, Task> mutate,
        CancellationToken cancellationToken,
        Func<BoardLocalRow, bool>? canMutate = null,
        bool deleteAfterSave = false)
    {
        var row = await local.FindItemAsync(userKey, op.ItemId, cancellationToken);
        if (row is null || (op.Section is not null && row.Section != op.Section))
        {
            return null;
        }

        if (canMutate is not null && !canMutate(row))
        {
            return null;
        }

        var expected = row.ServerUpdatedAtUtc;
        await mutate(row);
        if (deleteAfterSave)
        {
            await local.DeleteItemAsync(userKey, row.Id, cancellationToken);
        }
        else
        {
            await local.UpsertItemAsync(row, cancellationToken);
        }

        await EnqueueStaticAsync(local, userKey, op.Kind, op.PayloadFactory(row, expected), cancellationToken);
        return row.ToModel();
    }

    private static async Task HandleSortOrderUpdateAsync(
        IBoardLocalStore local,
        string userKey,
        BoardSection section,
        Guid itemId,
        double? sortOrder,
        BoardLocalRow row,
        CancellationToken cancellationToken)
    {
        if (!sortOrder.HasValue)
        {
            return;
        }

        row.SortOrder = sortOrder.Value;

        var items = await local.ListItemsAsync(userKey, includeArchived: false, cancellationToken);
        var needsRebalance = items.Any(x => x.Section == section
            && x.Id != itemId
            && Math.Abs((x.SortOrder ?? 0.0) - sortOrder.Value) < 1e-9);
        if (needsRebalance)
        {
            await RebalanceLocalSortOrdersAsync(local, userKey, section, cancellationToken);
        }
    }

    public Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        UpdateHabitArgs args,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (local, userKey) =>
            {
                var today = await TodayAsync(cancellationToken);
                return await UpdateRowAsync(
                    local,
                    userKey,
                    new RowUpdateOp(itemId, BoardSection.Habit, BoardOutboxOperationKind.UpdateHabit, (row, expected) => new UpdateHabitOutboxPayload(
                        itemId,
                        row.Title,
                        row.Notes,
                        row.Tags,
                        row.TrackPlus,
                        row.TrackMinus,
                        row.ResetPeriod,
                        row.Counter,
                        row.NegativeCounter,
                        row.ChecklistJson,
                        expected,
                        row.SortOrder)),
                    async row =>
                    {
                        row.Title = ZalgoSanitizer.SanitizeAndTrim(args.Title);
                        row.Notes = string.IsNullOrWhiteSpace(args.Notes) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Notes);
                        row.Tags = string.IsNullOrWhiteSpace(args.Tags) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Tags);
                        row.TrackPlus = args.TrackPlus;
                        row.TrackMinus = args.TrackMinus;
                        row.ResetPeriod = args.ResetPeriod;
                        row.Counter = args.Counter;
                        row.NegativeCounter = args.NegativeCounter;
                        row.HabitPeriodStart = HabitResetSchedule.PeriodStartFor(today, args.ResetPeriod);
                        row.ChecklistJson = DailyChecklistJson.Normalize(args.ChecklistJson);
                        await HandleSortOrderUpdateAsync(local, userKey, BoardSection.Habit, itemId, args.SortOrder, row, cancellationToken);
                    },
                    cancellationToken);
            },
            cancellationToken);

    public Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        UpdateTodoArgs args,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (local, userKey) => await UpdateRowAsync(
                local,
                userKey,
                new RowUpdateOp(itemId, BoardSection.Todo, BoardOutboxOperationKind.UpdateTodo, (row, expected) => new UpdateTodoOutboxPayload(
                    itemId,
                    row.Title,
                    row.Notes,
                    row.Tags,
                    row.ChecklistJson,
                    args.DueDate,
                    expected,
                    row.SortOrder)),
                async row =>
                {
                    row.Title = ZalgoSanitizer.SanitizeAndTrim(args.Title);
                    row.Notes = string.IsNullOrWhiteSpace(args.Notes) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Notes);
                    row.Tags = string.IsNullOrWhiteSpace(args.Tags) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Tags);
                    row.ChecklistJson = DailyChecklistJson.Normalize(args.ChecklistJson);
                    row.TodoDueDate = args.DueDate;
                    await HandleSortOrderUpdateAsync(local, userKey, BoardSection.Todo, itemId, args.SortOrder, row, cancellationToken);
                },
                cancellationToken),
            cancellationToken);

    public Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        UpdateDailyArgs args,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (local, userKey) => await UpdateRowAsync(
                local,
                userKey,
                new RowUpdateOp(itemId, BoardSection.Daily, BoardOutboxOperationKind.UpdateDaily, (row, expected) => new UpdateDailyOutboxPayload(
                    itemId,
                    row.Title,
                    row.Notes,
                    row.Tags,
                    args.StartDate,
                    args.Repeat,
                    args.RepeatInterval,
                    row.ChecklistJson,
                    args.Counter,
                    expected,
                    row.SortOrder)),
                async row =>
                {
                    row.Title = ZalgoSanitizer.SanitizeAndTrim(args.Title);
                    row.Notes = string.IsNullOrWhiteSpace(args.Notes) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Notes);
                    row.Tags = string.IsNullOrWhiteSpace(args.Tags) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Tags);
                    row.DailyStartDate = args.StartDate;
                    row.DailyRepeat = args.Repeat;
                    row.DailyRepeatInterval = args.RepeatInterval;
                    row.ChecklistJson = DailyChecklistJson.Normalize(args.ChecklistJson);
                    row.Counter = args.Counter;
                    await HandleSortOrderUpdateAsync(local, userKey, BoardSection.Daily, itemId, args.SortOrder, row, cancellationToken);
                },
                cancellationToken),
            cancellationToken);

    private static async Task<double> GetInitialLocalSortOrderAsync(
        IBoardLocalStore local,
        string userKey,
        BoardSection section,
        CancellationToken cancellationToken)
    {
        var items = await local.ListItemsAsync(userKey, includeArchived: true, cancellationToken);
        double? min = null;
        foreach (var item in items)
        {
            if (item.Section != section)
            {
                continue;
            }

            if (min is null || (item.SortOrder ?? double.MaxValue) < min)
            {
                min = item.SortOrder;
            }
        }

        return BoardItemReorder.SortOrderForNewItem(min);
    }

    private static async Task RebalanceLocalSortOrdersAsync(
        IBoardLocalStore local,
        string userKey,
        BoardSection section,
        CancellationToken cancellationToken)
    {
        var items = (await local.ListItemsAsync(userKey, includeArchived: false, cancellationToken))
            .Where(x => x.Section == section)
            .OrderBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Id)
            .ToList();

        var seq = 1.0;
        foreach (var item in items)
        {
            item.SortOrder = seq;
            seq += 1.0;
            await local.UpsertItemAsync(item, cancellationToken);
        }
    }

    private async Task AppendLocalActivityAsync(ActivityEventType type, Guid? boardItemId, string? titleSnapshot, CancellationToken cancellationToken)
    {
        try
        {
            var storeService = services.GetService<IActivityEventStore>();
            var clock = services.GetService<IClock>();
            if (storeService == null || clock == null)
            {
                return;
            }

            var rec = new UserActivityEventRecord(clock.UtcNow, type, boardItemId, null, titleSnapshot);
            await storeService.AppendAsync(rec, cancellationToken);
        }
        catch
        {
            // Best-effort local activity log for offline stats
        }
    }
}
