using App.MAUI.Data;
using App.Shared.RCL;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Remote;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.MAUI.Services.LocalBoard;

/// <summary>SQLite-backed board with outbound outbox; network I/O is driven by <see cref="MauiBoardSyncCoordinator" />.</summary>
public sealed partial class LocalFirstBoardDataService(
    IDbContextFactory<LocalBoardDbContext> dbFactory,
    IAuthTokenStore tokens,
    RemoteBoardDataService remote,
    IServiceProvider services,
    IUserTimeZoneService timeZone,
    MauiBoardSyncStatus syncStatus,
    ILogger<LocalFirstBoardDataService> logger)
    : IBoardDataService, IMauiBoardLocalStoreLifecycle, IDisposable
{
    private static volatile bool _schemaReady;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task EnsureStoreReadyAsync(CancellationToken cancellationToken = default) =>
        EnsureLocalStoreSchemaAsync(cancellationToken);

    public async Task ClearAllLocalStateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLocalStoreSchemaAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.BoardItems.ExecuteDeleteAsync(cancellationToken);
            await db.Outbox.ExecuteDeleteAsync(cancellationToken);
            var meta = await db.Meta.SingleOrDefaultAsync(m => m.Id == 1, cancellationToken);
            if (meta is null)
            {
                db.Meta.Add(new LocalBoardStoreMetaRow { Id = 1, BoundUserKey = null });
            }
            else
            {
                meta.BoundUserKey = null;
                meta.LastSyncCursorUtc = null;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLocalStoreSchemaAsync(cancellationToken);

        string? userKey = null;
        BoardSnapshot snap;
        var shouldFetchRemote = false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!await HasAuthAsync(cancellationToken))
            {
                return EmptySnapshot();
            }

            userKey = await ResolveUserKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(userKey))
            {
                return EmptySnapshot();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureUserScopeAsync(db, userKey, cancellationToken);
            snap = ReadSnapshot(db, userKey);

            if (IsEmpty(snap) && !syncStatus.IsSyncing)
            {
                shouldFetchRemote = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (shouldFetchRemote && !string.IsNullOrWhiteSpace(userKey))
        {
            BoardSnapshot? fresh = null;
            try
            {
                // Network HTTP call happens OUTSIDE the _gate lock:
                fresh = await remote.GetSnapshotAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Initial board hydrate from API skipped (offline or error).");
            }

            if (fresh is not null)
            {
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                    await ReplaceMirrorAsync(db, userKey, fresh, cancellationToken);
                    snap = ReadSnapshot(db, userKey);
                }
                finally
                {
                    _gate.Release();
                }
            }
        }

        return snap;
    }

    public async Task<BoardItem?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await EnsureLocalStoreSchemaAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!await HasAuthAsync(cancellationToken))
            {
                return null;
            }

            var userKey = await ResolveUserKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(userKey))
            {
                return null;
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureUserScopeAsync(db, userKey, cancellationToken);
            var row = await db.BoardItems.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && !x.IsArchived,
                    cancellationToken);
            return row?.ToModel();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Dictionary<Guid, int>> GetStreakMapAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLocalStoreSchemaAsync(cancellationToken);
        var userKey = await ResolveUserKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(userKey))
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var dailies = await db.BoardItems
                .Where(x => x.UserKey == userKey && !x.IsArchived && x.Section == BoardSection.Daily)
                .Select(x => new { x.Id, x.Counter })
                .ToListAsync(cancellationToken);
            return dailies.ToDictionary(x => x.Id, x => x.Counter);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLocalStoreSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady)
        {
            return;
        }

        await SchemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            try
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogTrace(ex, "Failed to enable WAL mode.");
            }
            await EnsureSqliteBoardColumnsAsync(db, cancellationToken);
            MarkSchemaReady();
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private static void MarkSchemaReady()
    {
        _schemaReady = true;
    }

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
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId,
                    cancellationToken);
                if (row is null)
                {
                    return null;
                }

                var expected = row.ServerUpdatedAtUtc;
                row.Title = ZalgoSanitizer.SanitizeAndTrim(title);
                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.Rename,
                    new RenameOutboxPayload(section, itemId, row.Title, expected));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            },
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

                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId,
                    cancellationToken);
                if (row is null)
                {
                    return false;
                }

                var expected = row.ServerUpdatedAtUtc;
                db.BoardItems.Remove(row);
                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.Delete,
                    new SectionItemOutboxPayload(section, itemId, expected));
                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);

    public Task<BoardItem?> ArchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId,
                    cancellationToken);
                if (row is null)
                {
                    return null;
                }

                var expected = row.ServerUpdatedAtUtc;
                row.IsArchived = true;
                row.ServerUpdatedAtUtc = DateTimeOffset.UtcNow;

                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.Archive,
                    new SectionItemOutboxPayload(section, itemId, expected));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            },
            cancellationToken);

    public Task<BoardItem?> UnarchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId,
                    cancellationToken);
                if (row is null)
                {
                    return null;
                }

                var expected = row.ServerUpdatedAtUtc;
                row.IsArchived = false;
                row.ServerUpdatedAtUtc = DateTimeOffset.UtcNow;

                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.Unarchive,
                    new SectionItemOutboxPayload(section, itemId, expected));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            },
            cancellationToken);

    public async Task<BoardSnapshot> GetArchivedSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLocalStoreSchemaAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var userKey = await ResolveUserKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(userKey))
        {
            return EmptySnapshot();
        }

        var today = DailySchedule.LocalToday(timeZone);
        var items = await db.BoardItems.AsNoTracking().Where(x => x.UserKey == userKey && x.IsArchived).ToListAsync(cancellationToken);

        List<BoardItem> habits = [.. items.Where(x => x.Section == BoardSection.Habit)
            .OrderBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue)
            .Select(x => x.ToModel())];
        List<BoardItem> dailies = [.. items.Where(x => x.Section == BoardSection.Daily)
            .OrderBy(x => IsDailyRowCompleteForToday(x, today) ? 1 : 0)
            .ThenBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue)
            .Select(x => x.ToModel())];
        List<BoardItem> todos = [.. items.Where(x => x.Section == BoardSection.Todo)
            .OrderBy(x => x.IsCompleted ? 1 : 0)
            .ThenBy(x => x.TodoDueDate.HasValue ? 1 : 0)
            .ThenBy(x => x.TodoDueDate ?? DateOnly.MaxValue)
            .ThenBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue)
            .Select(x => x.ToModel())];

        return new(habits, dailies, todos);
    }

    public Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId,
                    cancellationToken);
                if (row is null)
                {
                    return null;
                }

                var expected = row.ServerUpdatedAtUtc;
                ApplyLocalToggle(section, row);
                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.Toggle,
                    new SectionItemOutboxPayload(section, itemId, expected));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            },
            cancellationToken);

    public Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && x.Section == BoardSection.Daily,
                    cancellationToken);
                if (row is null)
                {
                    return null;
                }

                var expected = row.ServerUpdatedAtUtc;
                row.DailyLastCompletedOn = completedOn;
                row.IsCompleted = true;
                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.CompleteDailyForDate,
                    new CompleteDailyOutboxPayload(itemId, completedOn, expected));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            },
            cancellationToken);

    public Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && x.Section == BoardSection.Habit,
                    cancellationToken);
                if (row is null)
                {
                    return null;
                }

                var expectedInc = row.ServerUpdatedAtUtc;
                if (row.TrackPlus)
                {
                    row.Counter++;
                }

                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.HabitIncrement,
                    new ItemIdOutboxPayload(itemId, expectedInc));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            },
            cancellationToken);

    public Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && x.Section == BoardSection.Habit,
                    cancellationToken);
                if (row is null)
                {
                    return null;
                }

                var expectedDec = row.ServerUpdatedAtUtc;
                if (row.TrackMinus)
                {
                    row.NegativeCounter++;
                }

                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.HabitDecrement,
                    new ItemIdOutboxPayload(itemId, expectedDec));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            },
            cancellationToken);

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
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && x.Section == BoardSection.Habit,
                    cancellationToken);
                if (row is null)
                {
                    return null;
                }

                var expected = row.ServerUpdatedAtUtc;
                row.Title = ZalgoSanitizer.SanitizeAndTrim(args.Title);
                row.Notes = string.IsNullOrWhiteSpace(args.Notes) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Notes);
                row.Tags = string.IsNullOrWhiteSpace(args.Tags) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Tags);
                row.TrackPlus = args.TrackPlus;
                row.TrackMinus = args.TrackMinus;
                row.ResetPeriod = args.ResetPeriod;
                row.Counter = args.Counter;
                row.NegativeCounter = args.NegativeCounter;
                row.ChecklistJson = string.IsNullOrWhiteSpace(args.ChecklistJson)
                    ? null
                    : DailyChecklistJson.Serialize(DailyChecklistJson.Parse(args.ChecklistJson));

                await HandleSortOrderUpdateAsync(db, userKey, BoardSection.Habit, itemId, args.SortOrder, row, cancellationToken);

                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.UpdateHabit,
                    new UpdateHabitOutboxPayload(
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
                        row.SortOrder));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            },
            cancellationToken);

    public Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        UpdateTodoArgs args,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && x.Section == BoardSection.Todo,
                    cancellationToken);
                if (row is null)
                {
                    return null;
                }

                var expectedTodo = row.ServerUpdatedAtUtc;
                row.Title = ZalgoSanitizer.SanitizeAndTrim(args.Title);
                row.Notes = string.IsNullOrWhiteSpace(args.Notes) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Notes);
                row.Tags = string.IsNullOrWhiteSpace(args.Tags) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Tags);
                row.ChecklistJson = string.IsNullOrWhiteSpace(args.ChecklistJson)
                    ? null
                    : DailyChecklistJson.Serialize(DailyChecklistJson.Parse(args.ChecklistJson));
                row.TodoDueDate = args.DueDate.HasValue ? DateOnly.FromDateTime(args.DueDate.Value.Date) : null;

                await HandleSortOrderUpdateAsync(db, userKey, BoardSection.Todo, itemId, args.SortOrder, row, cancellationToken);

                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.UpdateTodo,
                    new UpdateTodoOutboxPayload(
                        itemId,
                        row.Title,
                        row.Notes,
                        row.Tags,
                        row.ChecklistJson,
                        args.DueDate,
                        expectedTodo,
                        row.SortOrder));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            },
            cancellationToken);

    public Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        UpdateDailyArgs args,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && x.Section == BoardSection.Daily,
                    cancellationToken);
                if (row is null)
                {
                    return null;
                }

                var expectedDaily = row.ServerUpdatedAtUtc;
                row.Title = ZalgoSanitizer.SanitizeAndTrim(args.Title);
                row.Notes = string.IsNullOrWhiteSpace(args.Notes) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Notes);
                row.Tags = string.IsNullOrWhiteSpace(args.Tags) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Tags);
                row.DailyStartDate = args.StartDate.HasValue ? DateOnly.FromDateTime(args.StartDate.Value.Date) : null;
                row.DailyRepeat = args.RepeatType;
                row.DailyRepeatInterval = args.RepeatInterval;
                row.ChecklistJson = string.IsNullOrWhiteSpace(args.ChecklistJson)
                    ? null
                    : DailyChecklistJson.Serialize(DailyChecklistJson.Parse(args.ChecklistJson));
                row.Counter = args.Streak;

                await HandleSortOrderUpdateAsync(db, userKey, BoardSection.Daily, itemId, args.SortOrder, row, cancellationToken);

                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.UpdateDaily,
                    new UpdateDailyOutboxPayload(
                        itemId,
                        row.Title,
                        row.Notes,
                        row.Tags,
                        args.StartDate,
                        args.RepeatType,
                        args.RepeatInterval,
                        row.ChecklistJson,
                        args.Streak,
                        expectedDaily,
                        row.SortOrder));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            },
            cancellationToken);

    /// <summary>Runs at most one outbox HTTP operation. Returns true if an entry was processed successfully.</summary>
    public async Task<bool> TryDrainOneOutboxOperationAsync(CancellationToken cancellationToken = default)
    {
        Guid operationId;

        await EnsureLocalStoreSchemaAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (!await HasAuthAsync(cancellationToken))
            {
                return false;
            }

            var userKey = await ResolveUserKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(userKey))
            {
                return false;
            }

            await EnsureUserScopeAsync(db, userKey, cancellationToken);

            var head = await db.Outbox
                .Where(o => o.UserKey == userKey)
                .OrderBy(o => o.CreatedAtUtc)
                .ThenBy(o => o.OperationId)
                .FirstOrDefaultAsync(cancellationToken);

            if (head is null)
            {
                return false;
            }

            if (head.AttemptCount > 0 && head.LastAttemptUtc is { } last)
            {
                var wait = Backoff(head.AttemptCount);
                if (DateTime.UtcNow < last + wait)
                {
                    return false;
                }
            }

            operationId = head.OperationId;
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await ExecuteOutboxRemoteByIdAsync(operationId, remote, cancellationToken);

            await _gate.WaitAsync(cancellationToken);
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                var still = await db.Outbox.FindAsync([operationId], cancellationToken);
                if (still is not null)
                {
                    db.Outbox.Remove(still);
                }

                await db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _gate.Release();
            }

            return true;
        }
        catch (BoardRemoteConflictException ex)
        {
            return await HandleRemoteConflictAsync(operationId, ex, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outbox operation {OperationId} failed.", operationId);
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                var still = await db.Outbox.FindAsync([operationId], cancellationToken);
                if (still is not null)
                {
                    still.AttemptCount++;
                    still.LastAttemptUtc = DateTime.UtcNow;
                    still.LastError = ex.Message;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            finally
            {
                _gate.Release();
            }

            return false;
        }
    }

    private static bool AreItemsContentEqual(BoardItem a, BoardItem b)
    {
        return string.Equals(a.Title, b.Title, StringComparison.Ordinal) &&
               a.IsCompleted == b.IsCompleted &&
               a.Counter == b.Counter &&
               string.Equals(a.Notes ?? string.Empty, b.Notes ?? string.Empty, StringComparison.Ordinal) &&
               string.Equals(a.Tags ?? string.Empty, b.Tags ?? string.Empty, StringComparison.Ordinal) &&
               a.TrackPlus == b.TrackPlus &&
               a.TrackMinus == b.TrackMinus &&
               a.NegativeCounter == b.NegativeCounter &&
               a.ResetPeriod == b.ResetPeriod &&
               a.DailyStartDate == b.DailyStartDate &&
               a.DailyRepeat == b.DailyRepeat &&
               a.DailyRepeatInterval == b.DailyRepeatInterval &&
               string.Equals(a.ChecklistJson ?? string.Empty, b.ChecklistJson ?? string.Empty, StringComparison.Ordinal) &&
               a.DailyLastCompletedOn == b.DailyLastCompletedOn &&
               a.TodoDueDate == b.TodoDueDate &&
               NullableDoubleEquals(a.SortOrder, b.SortOrder) &&
               a.IsArchived == b.IsArchived;
    }

    private static bool NullableDoubleEquals(double? a, double? b)
    {
        if (a is null && b is null)
        {
            return true;
        }
        if (a is null || b is null)
        {
            return false;
        }
        return Math.Abs(a.Value - b.Value) < 0.0001;
    }

    private async Task<bool> HandleRemoteConflictAsync(
        Guid operationId,
        BoardRemoteConflictException ex,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(ex, "Outbox operation {OperationId} returned 409 Conflict.", operationId);

        var serverItem = ex.ServerItem;
        if (serverItem is null)
        {
            logger.LogWarning("Conflict exception has no server item; dropping op.");
            await DropOutboxOperationAsync(operationId, cancellationToken);
            RequestSyncSoon();
            return false;
        }

        BoardItem? localItem = null;
        var section = BoardSection.Todo;
        var localTime = DateTimeOffset.MinValue;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.BoardItems.FindAsync([serverItem.Id], cancellationToken);
            if (row is not null)
            {
                localItem = row.ToModel();
                section = row.Section;
            }

            var opRow = await db.Outbox.FindAsync([operationId], cancellationToken);
            if (opRow is not null)
            {
                localTime = new DateTimeOffset(opRow.CreatedAtUtc, TimeSpan.Zero);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (localItem is null)
        {
            logger.LogWarning("Local item not found for conflict resolution; dropping op.");
            await DropOutboxOperationAsync(operationId, cancellationToken);
            RequestSyncSoon();
            return false;
        }

        // 1. Content-Aware check: if user-facing fields match exactly, resolve keeping Server version silently.
        if (AreItemsContentEqual(localItem, serverItem))
        {
            logger.LogInformation("Conflict detected but items are content-identical. Auto-resolving by keeping Server version silently.");
            await ResolveConflictKeepServerAsync(operationId, serverItem, section, cancellationToken);
            return false;
        }

        // 2. Last-Write-Wins (LWW) check: compare local update enqueued time against server updated timestamp.
        var serverTime = serverItem.ServerUpdatedAtUtc ?? DateTimeOffset.MinValue;
        if (localTime >= serverTime)
        {
            logger.LogInformation("Conflict auto-resolved via Last-Write-Wins: Keeping Device version (Local: {LocalTime} >= Server: {ServerTime}).", localTime, serverTime);
            await ResolveConflictKeepMineAsync(operationId, serverItem, cancellationToken);
        }
        else
        {
            logger.LogInformation("Conflict auto-resolved via Last-Write-Wins: Keeping Server version (Local: {LocalTime} < Server: {ServerTime}).", localTime, serverTime);
            await ResolveConflictKeepServerAsync(operationId, serverItem, section, cancellationToken);
        }

        return false;
    }

    private async Task ResolveConflictKeepMineAsync(Guid operationId, BoardItem serverItem, CancellationToken cancellationToken)
    {
        logger.LogInformation("Conflict resolved by user: Keeping Device version.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var opRow = await db.Outbox.FindAsync([operationId], cancellationToken);
            if (opRow is not null)
            {
                var updatedPayload = BoardOutboxPayloadMapper.RemapExpectedVersion(
                    opRow.Kind,
                    opRow.PayloadJson,
                    serverItem.ServerUpdatedAtUtc ?? DateTimeOffset.UtcNow);
                opRow.PayloadJson = updatedPayload;
                opRow.AttemptCount = 0;
                opRow.LastError = null;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ResolveConflictKeepServerAsync(
        Guid operationId,
        BoardItem serverItem,
        BoardSection section,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Conflict resolved by user: Keeping Server version.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var opRow = await db.Outbox.FindAsync([operationId], cancellationToken);
            if (opRow is not null)
            {
                db.Outbox.Remove(opRow);
            }

            var localRow = await db.BoardItems.FindAsync([serverItem.Id], cancellationToken);
            if (localRow is not null)
            {
                var userKey = localRow.UserKey;
                var updated = LocalBoardItemRow.FromModel(section, userKey, serverItem, false);
                localRow.Title = updated.Title;
                localRow.IsCompleted = updated.IsCompleted;
                localRow.Counter = updated.Counter;
                localRow.Notes = updated.Notes;
                localRow.Tags = updated.Tags;
                localRow.TrackPlus = updated.TrackPlus;
                localRow.TrackMinus = updated.TrackMinus;
                localRow.NegativeCounter = updated.NegativeCounter;
                localRow.ResetPeriod = updated.ResetPeriod;
                localRow.DailyStartDate = updated.DailyStartDate;
                localRow.DailyRepeat = updated.DailyRepeat;
                localRow.DailyRepeatInterval = updated.DailyRepeatInterval;
                localRow.ChecklistJson = updated.ChecklistJson;
                localRow.DailyLastCompletedOn = updated.DailyLastCompletedOn;
                localRow.TodoDueDate = updated.TodoDueDate;
                localRow.ServerUpdatedAtUtc = updated.ServerUpdatedAtUtc;
                localRow.CreatedAtUtc = updated.CreatedAtUtc;
                localRow.SortOrder = updated.SortOrder;
                localRow.IsArchived = updated.IsArchived;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        RequestSyncSoon();
    }

    public async Task<string?> TryGetStuckOutboxHintAsync(int minAttempts, CancellationToken cancellationToken = default)
    {
        await EnsureLocalStoreSchemaAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (!await HasAuthAsync(cancellationToken))
            {
                return null;
            }

            var userKey = await ResolveUserKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(userKey))
            {
                return null;
            }

            var row = await db.Outbox
                .Where(o => o.UserKey == userKey && o.AttemptCount >= minAttempts)
                .OrderByDescending(o => o.AttemptCount)
                .FirstOrDefaultAsync(cancellationToken);

            if (row is null)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(row.LastError)
                ? "Some changes could not sync. Try again when online."
                : $"Sync issue: {row.LastError}";
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryPullRemoteMirrorAsync(CancellationToken cancellationToken = default)
    {
        var userKey = await ResolveUserKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(userKey) || !await HasAuthAsync(cancellationToken))
        {
            return false;
        }

        await EnsureLocalStoreSchemaAsync(cancellationToken);

        string? cursor;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureUserScopeAsync(db, userKey, cancellationToken);

            var metaRow = await db.Meta.SingleOrDefaultAsync(m => m.Id == 1, cancellationToken);
            cursor = metaRow?.LastSyncCursorUtc;
        }
        finally
        {
            _gate.Release();
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            (var hasResult, var success) = await TryPullDeltaMirrorAsync(userKey, cursor, cancellationToken);
            if (hasResult)
            {
                return success;
            }
        }

        return await TryPullSnapshotMirrorAsync(userKey, cancellationToken);
    }

    private async Task<(bool HasResult, bool Success)> TryPullDeltaMirrorAsync(string userKey, string cursor, CancellationToken cancellationToken)
    {
        BoardSyncDelta? delta;
        try
        {
            delta = await remote.TryGetSyncDeltaAsync(cursor, cancellationToken);
        }
        catch
        {
            return (true, false);
        }

        if (delta is null)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                if (await db.Meta.SingleOrDefaultAsync(m => m.Id == 1, cancellationToken) is { } metaRow)
                {
                    metaRow.LastSyncCursorUtc = null;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            finally
            {
                _gate.Release();
            }

            return (false, false);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var pending = await db.Outbox
                .Where(o => o.UserKey == userKey)
                .ToListAsync(cancellationToken);
            var skipIds = BoardOutboxReferencedIds.CollectFromPayloads(
                pending.Select(p => (p.Kind, p.PayloadJson)));

            await ApplySyncDeltaAsync(db, userKey, delta, skipIds, cancellationToken);

            if (await db.Meta.SingleOrDefaultAsync(m => m.Id == 1, cancellationToken) is { } metaRow)
            {
                metaRow.LastSyncCursorUtc = delta.NextCursor;
            }

            await db.SaveChangesAsync(cancellationToken);
            return (true, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> TryPullSnapshotMirrorAsync(string userKey, CancellationToken cancellationToken)
    {
        BoardSnapshot snap;
        try
        {
            snap = await remote.GetSnapshotAsync(cancellationToken);
        }
        catch
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await db.Outbox.AnyAsync(o => o.UserKey == userKey, cancellationToken))
            {
                return false;
            }

            await ReplaceMirrorAsync(db, userKey, snap, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExecuteOutboxRemoteByIdAsync(Guid operationId, RemoteBoardDataService api,
        CancellationToken cancellationToken)
    {
        BoardOutboxRow head;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            head = await db.Outbox.FindAsync([operationId], cancellationToken)
                   ?? throw new InvalidOperationException("Outbox entry disappeared.");
        }
        finally
        {
            _gate.Release();
        }

        await ExecuteOutboxRemoteAsync(head, api, cancellationToken);
    }

    private async Task ExecuteOutboxRemoteAsync(BoardOutboxRow head, RemoteBoardDataService api,
        CancellationToken cancellationToken)
    {
        switch (head.Kind)
        {
            case BoardOutboxOperationKind.Create:
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<CreateOutboxPayload>(head.PayloadJson, BoardOutboxJson.Options)
                            ?? throw new InvalidOperationException("Invalid create payload.");
                    var serverItem = await api.CreateItemAsync(p.Section, p.Title, p.ClientItemId, head.OperationId, cancellationToken);
                    await CommitCreateSuccessAsync(p.ClientItemId, p.Section, serverItem, head.UserKey, cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.Rename:
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<RenameOutboxPayload>(head.PayloadJson, BoardOutboxJson.Options)
                            ?? throw new InvalidOperationException("Invalid rename payload.");
                    var updated = await api.RenameItemAsync(
                        p.Section,
                        p.ItemId,
                        p.Title,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await PatchLocalAsync(p.ItemId, head.UserKey, updated, cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.Delete:
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<SectionItemOutboxPayload>(head.PayloadJson, BoardOutboxJson.Options)
                            ?? throw new InvalidOperationException("Invalid delete payload.");
                    _ = await api.DeleteItemAsync(
                        p.Section,
                        p.ItemId,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.Toggle:
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<SectionItemOutboxPayload>(head.PayloadJson, BoardOutboxJson.Options)
                            ?? throw new InvalidOperationException("Invalid toggle payload.");
                    var updated = await api.ToggleItemAsync(
                        p.Section,
                        p.ItemId,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await PatchLocalAsync(p.ItemId, head.UserKey, updated, cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.CompleteDailyForDate:
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<CompleteDailyOutboxPayload>(head.PayloadJson, BoardOutboxJson.Options)
                            ?? throw new InvalidOperationException("Invalid complete-daily payload.");
                    var updated = await api.CompleteDailyForDateAsync(
                        p.ItemId,
                        p.CompletedOn,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await PatchLocalAsync(p.ItemId, head.UserKey, updated, cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.HabitIncrement:
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<ItemIdOutboxPayload>(head.PayloadJson, BoardOutboxJson.Options)
                            ?? throw new InvalidOperationException("Invalid habit+ payload.");
                    var updated = await api.IncrementHabitPlusAsync(
                        p.ItemId,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await PatchLocalAsync(p.ItemId, head.UserKey, updated, cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.HabitDecrement:
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<ItemIdOutboxPayload>(head.PayloadJson, BoardOutboxJson.Options)
                            ?? throw new InvalidOperationException("Invalid habit− payload.");
                    var updated = await api.IncrementHabitMinusAsync(
                        p.ItemId,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await PatchLocalAsync(p.ItemId, head.UserKey, updated, cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.UpdateHabit:
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<UpdateHabitOutboxPayload>(head.PayloadJson, BoardOutboxJson.Options)
                            ?? throw new InvalidOperationException("Invalid habit update payload.");
                    var updated = await api.UpdateHabitAsync(
                        p.ItemId,
                        new UpdateHabitArgs(
                            p.Title,
                            p.Notes,
                            p.Tags,
                            p.TrackPlus,
                            p.TrackMinus,
                            p.ResetPeriod,
                            p.Counter,
                            p.NegativeCounter,
                            p.ChecklistJson,
                            p.SortOrder),
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await PatchLocalAsync(p.ItemId, head.UserKey, updated, cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.UpdateTodo:
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<UpdateTodoOutboxPayload>(head.PayloadJson, BoardOutboxJson.Options)
                            ?? throw new InvalidOperationException("Invalid todo update payload.");
                    var updated = await api.UpdateTodoAsync(
                        p.ItemId,
                        new UpdateTodoArgs(
                            p.Title,
                            p.Notes,
                            p.Tags,
                            p.ChecklistJson,
                            p.DueDate,
                            p.SortOrder),
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await PatchLocalAsync(p.ItemId, head.UserKey, updated, cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.UpdateDaily:
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<UpdateDailyOutboxPayload>(head.PayloadJson, BoardOutboxJson.Options)
                            ?? throw new InvalidOperationException("Invalid daily update payload.");
                    var updated = await api.UpdateDailyAsync(
                        p.ItemId,
                        new UpdateDailyArgs(
                            p.Title,
                            p.Notes,
                            p.Tags,
                            p.StartDate,
                            p.RepeatType,
                            p.RepeatInterval,
                            p.ChecklistJson,
                            p.Streak,
                            p.SortOrder),
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await PatchLocalAsync(p.ItemId, head.UserKey, updated, cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.Archive:
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<SectionItemOutboxPayload>(head.PayloadJson, BoardOutboxJson.Options)
                            ?? throw new InvalidOperationException("Invalid archive payload.");
                    var updated = await api.ArchiveItemAsync(
                        p.Section,
                        p.ItemId,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await PatchLocalAsync(p.ItemId, head.UserKey, updated, cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.Unarchive:
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<SectionItemOutboxPayload>(head.PayloadJson, BoardOutboxJson.Options)
                            ?? throw new InvalidOperationException("Invalid unarchive payload.");
                    var updated = await api.UnarchiveItemAsync(
                        p.Section,
                        p.ItemId,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await PatchLocalAsync(p.ItemId, head.UserKey, updated, cancellationToken);
                    return;
                }
            default:
                throw new InvalidOperationException($"Unknown outbox kind {head.Kind}.");
        }
    }

    private async Task DropOutboxOperationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var still = await db.Outbox.FindAsync([operationId], cancellationToken);
            if (still is not null)
            {
                db.Outbox.Remove(still);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CommitCreateSuccessAsync(Guid clientId, BoardSection section, BoardItem serverItem, string userKey,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var old = await db.BoardItems.FindAsync([clientId], cancellationToken);
            if (old is not null)
            {
                db.BoardItems.Remove(old);
            }

            db.BoardItems.Add(LocalBoardItemRow.FromModel(section, userKey, serverItem, false));

            foreach (var row in await db.Outbox.Where(o => o.UserKey == userKey).ToListAsync(cancellationToken))
            {
                row.PayloadJson = BoardOutboxPayloadMapper.RemapClientToServerId(row.Kind, row.PayloadJson, clientId, serverItem.Id);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PatchLocalAsync(Guid itemId, string userKey, BoardItem? serverItem,
        CancellationToken cancellationToken)
    {
        if (serverItem is null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.BoardItems.FirstOrDefaultAsync(
                x => x.UserKey == userKey && x.Id == itemId,
                cancellationToken);
            if (row is null)
            {
                return;
            }

            var section = row.Section;
            var awaiting = row.AwaitingServerCreate;
            var updated = LocalBoardItemRow.FromModel(section, userKey, serverItem, awaiting);
            row.Title = updated.Title;
            row.IsCompleted = updated.IsCompleted;
            row.Counter = updated.Counter;
            row.Notes = updated.Notes;
            row.Tags = updated.Tags;
            row.TrackPlus = updated.TrackPlus;
            row.TrackMinus = updated.TrackMinus;
            row.NegativeCounter = updated.NegativeCounter;
            row.ResetPeriod = updated.ResetPeriod;
            row.DailyStartDate = updated.DailyStartDate;
            row.DailyRepeat = updated.DailyRepeat;
            row.DailyRepeatInterval = updated.DailyRepeatInterval;
            row.ChecklistJson = updated.ChecklistJson;
            row.DailyLastCompletedOn = updated.DailyLastCompletedOn;
            row.TodoDueDate = updated.TodoDueDate;
            row.ServerUpdatedAtUtc = updated.ServerUpdatedAtUtc;
            row.CreatedAtUtc = updated.CreatedAtUtc;
            row.SortOrder = updated.SortOrder;
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ApplyLocalToggle(BoardSection section, LocalBoardItemRow row)
    {
        switch (section)
        {
            case BoardSection.Habit:
                row.IsCompleted = !row.IsCompleted;
                break;
            case BoardSection.Daily:
                {
                    var today = DailySchedule.LocalToday(timeZone);
                    var done = row.DailyLastCompletedOn == today || (row.DailyLastCompletedOn is null && row.IsCompleted);
                    if (done)
                    {
                        row.DailyLastCompletedOn = null;
                        row.IsCompleted = false;
                    }
                    else
                    {
                        row.DailyLastCompletedOn = today;
                        row.IsCompleted = true;
                    }

                    break;
                }
            case BoardSection.Todo:
                row.IsCompleted = !row.IsCompleted;
                break;
        }
    }

    private async Task<T> MutateAsync<T>(Func<LocalBoardDbContext, string, Task<T>> action, CancellationToken cancellationToken)
    {
        await EnsureLocalStoreSchemaAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (!await HasAuthAsync(cancellationToken))
            {
                throw new InvalidOperationException("Sign in to change your board.");
            }

            var userKey = await ResolveUserKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(userKey))
            {
                throw new InvalidOperationException("Sign in to change your board.");
            }

            await EnsureUserScopeAsync(db, userKey, cancellationToken);
            return await action(db, userKey);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> MutateWithSyncAsync<T>(Func<LocalBoardDbContext, string, Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await MutateAsync(action, cancellationToken);
        }
        finally
        {
            RequestSyncSoon();
        }
    }

    private void RequestSyncSoon()
    {
        try
        {
            services.GetRequiredService<MauiBoardSyncCoordinator>().RequestSync();
        }
        catch (Exception ex)
        {
            logger.LogTrace(ex, "Could not request board sync.");
        }
    }

    private static void Enqueue<T>(LocalBoardDbContext db, string userKey, BoardOutboxOperationKind kind, T payload)
    {
        db.Outbox.Add(
            new BoardOutboxRow
            {
                OperationId = Guid.NewGuid(),
                UserKey = userKey,
                Kind = kind,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload, BoardOutboxJson.Options),
                CreatedAtUtc = DateTime.UtcNow
            });
    }

    private static async Task<bool> TryCoalesceDeletePendingCreateAsync(LocalBoardDbContext db, string userKey, Guid itemId,
        CancellationToken cancellationToken)
    {
        var pending = await db.Outbox
            .Where(o => o.UserKey == userKey && o.Kind == BoardOutboxOperationKind.Create)
            .ToListAsync(cancellationToken);

        foreach (var row in pending)
        {
            var p = System.Text.Json.JsonSerializer.Deserialize<CreateOutboxPayload>(row.PayloadJson, BoardOutboxJson.Options);
            if (p?.ClientItemId != itemId)
            {
                continue;
            }

            db.Outbox.Remove(row);
            var entity = await db.BoardItems.FindAsync([itemId], cancellationToken);
            if (entity is not null)
            {
                db.BoardItems.Remove(entity);
            }

            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    private static async Task EnsureUserScopeAsync(LocalBoardDbContext db, string userKey, CancellationToken cancellationToken)
    {
        var meta = await db.Meta.SingleOrDefaultAsync(m => m.Id == 1, cancellationToken);
        if (meta is null)
        {
            db.Meta.Add(new LocalBoardStoreMetaRow { Id = 1, BoundUserKey = userKey });
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (string.Equals(meta.BoundUserKey, userKey, StringComparison.Ordinal))
        {
            return;
        }

        await db.BoardItems.ExecuteDeleteAsync(cancellationToken);
        await db.Outbox.ExecuteDeleteAsync(cancellationToken);
        meta.BoundUserKey = userKey;
        meta.LastSyncCursorUtc = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> ResolveUserKeyAsync(CancellationToken cancellationToken)
    {
        var email = await tokens.GetEmailAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(email))
        {
            return email.Trim().ToUpperInvariant();
        }

        var jwt = await tokens.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(jwt))
        {
            return null;
        }

        var fromJwt = JwtAccessTokenDisplayClaims.TryGetEmail(jwt);
        return string.IsNullOrWhiteSpace(fromJwt) ? null : fromJwt.Trim().ToUpperInvariant();
    }

    private async Task<bool> HasAuthAsync(CancellationToken cancellationToken) =>
        !string.IsNullOrEmpty(await tokens.GetAccessTokenAsync(cancellationToken));

    private BoardSnapshot ReadSnapshot(LocalBoardDbContext db, string userKey)
    {
        var today = DailySchedule.LocalToday(timeZone);
        List<LocalBoardItemRow> items = [.. db.BoardItems.AsNoTracking().Where(x => x.UserKey == userKey && !x.IsArchived)];
        // Match BoardPersistenceService.GetSnapshotAsync ordering (web app).
        List<BoardItem> habits = [.. items.Where(x => x.Section == BoardSection.Habit)
            .OrderBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Id)
            .Select(x => x.ToModel())];
        List<BoardItem> dailies = [.. items.Where(x => x.Section == BoardSection.Daily)
            .OrderBy(x => IsDailyRowCompleteForToday(x, today) ? 1 : 0)
            .ThenBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Id)
            .Select(x => x.ToModel())];
        List<BoardItem> todos = [.. items.Where(x => x.Section == BoardSection.Todo)
            .OrderBy(x => x.IsCompleted ? 1 : 0)
            .ThenBy(x => x.TodoDueDate.HasValue ? 1 : 0)
            .ThenBy(x => x.TodoDueDate ?? DateOnly.MaxValue)
            .ThenBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Id)
            .Select(x => x.ToModel())];
        return new(habits, dailies, todos);
    }

    private static bool IsDailyRowCompleteForToday(LocalBoardItemRow row, DateOnly today)
    {
        if (row.DailyLastCompletedOn is { } t && t == today)
        {
            return true;
        }
        return row.DailyLastCompletedOn is null && row.IsCompleted;
    }

    private static async Task ReplaceMirrorAsync(LocalBoardDbContext db, string userKey, BoardSnapshot snap,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.BoardItems.Where(x => x.UserKey == userKey).ExecuteDeleteAsync(cancellationToken);
            foreach (var h in snap.Habits)
            {
                db.BoardItems.Add(LocalBoardItemRow.FromModel(BoardSection.Habit, userKey, h, false));
            }

            foreach (var d in snap.Dailies)
            {
                db.BoardItems.Add(LocalBoardItemRow.FromModel(BoardSection.Daily, userKey, d, false));
            }

            foreach (var t in snap.Todos)
            {
                db.BoardItems.Add(LocalBoardItemRow.FromModel(BoardSection.Todo, userKey, t, false));
            }

            await db.SaveChangesAsync(cancellationToken);

            if (await db.Meta.SingleOrDefaultAsync(m => m.Id == 1, cancellationToken) is { } meta)
            {
                meta.LastSyncCursorUtc = ComputeMirrorCursor(snap);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsEmpty(BoardSnapshot s) =>
        s.Habits.Count == 0 && s.Dailies.Count == 0 && s.Todos.Count == 0;

    private static BoardSnapshot EmptySnapshot() => new([], [], []);

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Min(attempt, 8))));

    private static string ComputeMirrorCursor(BoardSnapshot snap)
    {
        DateTimeOffset? m = null;
        foreach (var x in snap.Habits.Concat(snap.Dailies).Concat(snap.Todos))
        {
            if (x.ServerUpdatedAtUtc is { } u)
            {
                m = m is null || u > m ? u : m;
            }
        }

        return (m ?? DateTimeOffset.UtcNow).ToString("O");
    }

    private static async Task EnsureSqliteBoardColumnsAsync(LocalBoardDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE BoardItems ADD COLUMN ServerUpdatedAtUtc TEXT NULL;",
                cancellationToken);
        }
        catch
        {
            // column already present
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE BoardItems ADD COLUMN CreatedAtUtc TEXT NULL;",
                cancellationToken);
        }
        catch
        {
            // column might already exist, safe to ignore.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE BoardItems ADD COLUMN SortOrder REAL NULL;",
                cancellationToken);
        }
        catch
        {
            // column might already exist, safe to ignore.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE BoardItems SET SortOrder = rowid WHERE SortOrder IS NULL;",
                cancellationToken);
        }
        catch
        {
            // column or data update might fail if already present, safe to ignore.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Meta ADD COLUMN LastSyncCursorUtc TEXT NULL;",
                cancellationToken);
        }
        catch
        {
            // column might already exist, safe to ignore.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE BoardItems ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;",
                cancellationToken);
        }
        catch
        {
            // column might already exist, safe to ignore.
        }
    }

    private static async Task ApplySyncDeltaAsync(
        LocalBoardDbContext db,
        string userKey,
        BoardSyncDelta delta,
        HashSet<Guid> skipIds,
        CancellationToken cancellationToken)
    {
        List<Guid> syncIds = [.. delta.Items
            .Select(x => x.Item.Id)
            .Where(id => !skipIds.Contains(id))];
        var existingRows = syncIds.Count == 0
            ? []
            : await db.BoardItems
                .Where(x => x.UserKey == userKey && syncIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var id in delta.DeletedItemIds)
        {
            if (skipIds.Contains(id))
            {
                continue;
            }
            await db.BoardItems.Where(x => x.UserKey == userKey && x.Id == id).ExecuteDeleteAsync(cancellationToken);
        }

        foreach (var entry in delta.Items)
        {
            if (skipIds.Contains(entry.Item.Id))
            {
                continue;
            }
            existingRows.TryGetValue(entry.Item.Id, out var row);
            if (row is null)
            {
                db.BoardItems.Add(LocalBoardItemRow.FromModel(entry.Section, userKey, entry.Item, false));
                continue;
            }

            var awaiting = row.AwaitingServerCreate;
            var upd = LocalBoardItemRow.FromModel(entry.Section, userKey, entry.Item, awaiting);
            row.Section = upd.Section;
            row.Title = upd.Title;
            row.IsCompleted = upd.IsCompleted;
            row.Counter = upd.Counter;
            row.Notes = upd.Notes;
            row.Tags = upd.Tags;
            row.TrackPlus = upd.TrackPlus;
            row.TrackMinus = upd.TrackMinus;
            row.NegativeCounter = upd.NegativeCounter;
            row.ResetPeriod = upd.ResetPeriod;
            row.DailyStartDate = upd.DailyStartDate;
            row.DailyRepeat = upd.DailyRepeat;
            row.DailyRepeatInterval = upd.DailyRepeatInterval;
            row.ChecklistJson = upd.ChecklistJson;
            row.DailyLastCompletedOn = upd.DailyLastCompletedOn;
            row.TodoDueDate = upd.TodoDueDate;
            row.ServerUpdatedAtUtc = upd.ServerUpdatedAtUtc;
            row.CreatedAtUtc = upd.CreatedAtUtc;
            row.SortOrder = upd.SortOrder;
        }
    }

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

    public void Dispose()
    {
        _gate.Dispose();
    }
}
