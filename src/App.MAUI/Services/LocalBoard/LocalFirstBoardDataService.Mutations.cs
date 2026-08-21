using App.MAUI.Data;
using App.Shared.RCL;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.EntityFrameworkCore;

namespace App.MAUI.Services.LocalBoard;

public sealed partial class LocalFirstBoardDataService
{
    public Task<BoardItem> CreateItemAsync(BoardSection section, string title, Guid? itemId = null,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) =>
            {
                var id = itemId ?? Guid.NewGuid();
                var now = DateTimeOffset.UtcNow;
                var sortOrder = await GetInitialLocalSortOrderAsync(db, userKey, section, cancellationToken);
                BoardItem item = new(id, ZalgoSanitizer.SanitizeAndTrim(title), CreatedAtUtc: now, SortOrder: sortOrder);
                db.BoardItems.Add(LocalBoardItemRow.FromModel(section, userKey, item, true));
                CreateOutboxPayload payload = new(section, item.Title, id);
                Enqueue(db, userKey, BoardOutboxOperationKind.Create, payload);
                await db.SaveChangesAsync(cancellationToken);
                return item;
            },
            cancellationToken);

    public Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) => await UpdateRowAsync(
                db,
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
            async (db, userKey) =>
            {
                if (await TryCoalesceDeletePendingCreateAsync(db, userKey, itemId, cancellationToken))
                {
                    return true;
                }

                return await UpdateRowAsync(
                    db,
                    userKey,
                    new RowUpdateOp(itemId, null, BoardOutboxOperationKind.Delete, (row, expected) => new SectionItemOutboxPayload(section, itemId, expected)),
                    row =>
                    {
                        db.BoardItems.Remove(row);
                        return Task.CompletedTask;
                    },
                    cancellationToken) is not null;
            },
            cancellationToken);

    public Task<BoardItem?> ArchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) => await UpdateRowAsync(
                db,
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
            async (db, userKey) => await UpdateRowAsync(
                db,
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
        await EnsureLocalStoreSchemaAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var userKey = await ResolveUserKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(userKey))
            {
                return EmptySnapshot();
            }

            var today = await TodayAsync(cancellationToken);
            var items = await db.BoardItems.AsNoTracking().Where(x => x.UserKey == userKey && x.IsArchived).ToListAsync(cancellationToken);

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
            async (db, userKey) =>
            {
                var today = await TodayAsync(cancellationToken);
                var result = await UpdateRowAsync(
                    db,
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
            async (db, userKey) =>
            {
                var result = await UpdateRowAsync(
                    db,
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

    /// <summary>Mirrors the server's <c>complete-for-date</c> guards so a rejected retro never queues an outbox op.</summary>
    private static bool CanCompleteDailyForDate(LocalBoardItemRow row, DateOnly completedOn, DateOnly today) =>
        DailySchedule.CanCompleteForDate(
            row.DailyStartDate, row.DailyRepeat, row.DailyRepeatInterval, row.DailyLastCompletedOn, completedOn, today);

    public Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) =>
            {
                var result = await UpdateRowAsync(
                    db,
                    userKey,
                    new RowUpdateOp(itemId, BoardSection.Habit, BoardOutboxOperationKind.HabitIncrement, (row, expected) => new ItemIdOutboxPayload(itemId, expected)),
                    row =>
                    {
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
            async (db, userKey) =>
            {
                var result = await UpdateRowAsync(
                    db,
                    userKey,
                    new RowUpdateOp(itemId, BoardSection.Habit, BoardOutboxOperationKind.HabitDecrement, (row, expected) => new ItemIdOutboxPayload(itemId, expected)),
                    row =>
                    {
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
        Func<LocalBoardItemRow, DateTimeOffset?, object> PayloadFactory);

    private static async Task<BoardItem?> UpdateRowAsync(
        LocalBoardDbContext db,
        string userKey,
        RowUpdateOp op,
        Func<LocalBoardItemRow, Task> mutate,
        CancellationToken cancellationToken,
        Func<LocalBoardItemRow, bool>? canMutate = null)
    {
        var query = db.BoardItems.Where(x => x.UserKey == userKey && x.Id == op.ItemId);
        if (op.Section is not null)
        {
            query = query.Where(x => x.Section == op.Section);
        }

        var row = await query.FirstOrDefaultAsync(cancellationToken);
        if (row is null || (canMutate is not null && !canMutate(row)))
        {
            return null;
        }

        var expected = row.ServerUpdatedAtUtc;
        await mutate(row);
        Enqueue(db, userKey, op.Kind, op.PayloadFactory(row, expected));
        await db.SaveChangesAsync(cancellationToken);
        return row.ToModel();
    }

    private static async Task HandleSortOrderUpdateAsync(
        LocalBoardDbContext db,
        string userKey,
        BoardSection section,
        Guid itemId,
        double? sortOrder,
        LocalBoardItemRow row,
        CancellationToken cancellationToken)
    {
        if (!sortOrder.HasValue)
        {
            return;
        }

        row.SortOrder = sortOrder.Value;

        var needsRebalance = await db.BoardItems
            .AnyAsync(x => x.UserKey == userKey
                        && x.Section == section
                        && x.Id != itemId
                        && Math.Abs((x.SortOrder ?? 0.0) - sortOrder.Value) < 1e-9,
                      cancellationToken);
        if (needsRebalance)
        {
            await RebalanceLocalSortOrdersAsync(db, userKey, section, cancellationToken);
        }
    }

    public Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        UpdateHabitArgs args,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) => await UpdateRowAsync(
                db,
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
                    row.ChecklistJson = DailyChecklistJson.Normalize(args.ChecklistJson);
                    await HandleSortOrderUpdateAsync(db, userKey, BoardSection.Habit, itemId, args.SortOrder, row, cancellationToken);
                },
                cancellationToken),
            cancellationToken);

    public Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        UpdateTodoArgs args,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) => await UpdateRowAsync(
                db,
                userKey,
                new RowUpdateOp(itemId, BoardSection.Todo, BoardOutboxOperationKind.UpdateTodo, (row, expected) => new UpdateTodoOutboxPayload(
                    itemId,
                    row.Title,
                    row.Notes,
                    row.Tags,
                    row.ChecklistJson,
                    args.DueDate,
                    expected,
                    row.SortOrder,
                    args.TodoRepeatIntervalDays)),
                async row =>
                {
                    row.Title = ZalgoSanitizer.SanitizeAndTrim(args.Title);
                    row.Notes = string.IsNullOrWhiteSpace(args.Notes) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Notes);
                    row.Tags = string.IsNullOrWhiteSpace(args.Tags) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Tags);
                    row.ChecklistJson = DailyChecklistJson.Normalize(args.ChecklistJson);
                    row.TodoDueDate = args.DueDate;
                    row.TodoRepeatIntervalDays = args.TodoRepeatIntervalDays;
                    await HandleSortOrderUpdateAsync(db, userKey, BoardSection.Todo, itemId, args.SortOrder, row, cancellationToken);
                },
                cancellationToken),
            cancellationToken);

    public Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        UpdateDailyArgs args,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) => await UpdateRowAsync(
                db,
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
                    await HandleSortOrderUpdateAsync(db, userKey, BoardSection.Daily, itemId, args.SortOrder, row, cancellationToken);
                },
                cancellationToken),
            cancellationToken);

    /// <summary>Runs at most one outbox HTTP operation. Returns true if an entry was processed successfully.</summary>

    private static async Task<double> GetInitialLocalSortOrderAsync(
        LocalBoardDbContext db,
        string userKey,
        BoardSection section,
        CancellationToken cancellationToken)
    {
        var min = await db.BoardItems
            .Where(x => x.UserKey == userKey && x.Section == section)
            .Select(x => x.SortOrder)
            .MinAsync(cancellationToken);
        return BoardItemReorder.SortOrderForNewItem(min);
    }

    private static async Task RebalanceLocalSortOrdersAsync(
        LocalBoardDbContext db,
        string userKey,
        BoardSection section,
        CancellationToken cancellationToken)
    {
        var items = await db.BoardItems
            .Where(x => x.UserKey == userKey && x.Section == section)
            .OrderBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var seq = 1.0;
        foreach (var item in items)
        {
            item.SortOrder = seq;
            seq += 1.0;
        }
    }

    private async Task AppendLocalActivityAsync(ActivityEventType type, Guid? boardItemId, string? titleSnapshot, CancellationToken cancellationToken)
    {
        try
        {
            var store = services.GetService<IActivityEventStore>();
            var clock = services.GetService<IClock>();
            if (store == null || clock == null)
            {
                return;
            }

            var rec = new UserActivityEventRecord(clock.UtcNow, type, boardItemId, null, titleSnapshot);
            await store.AppendAsync(rec, cancellationToken);
        }
        catch
        {
            // Best-effort local activity log for offline stats
        }
    }

}
