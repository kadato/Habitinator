using App.MAUI.Data;
using App.MAUI.Services;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.MAUI.Services.LocalBoard;

/// <summary>SQLite-backed board with outbound outbox; network I/O is driven by <see cref="MauiBoardSyncCoordinator" />.</summary>
public sealed class LocalFirstBoardDataService(
    IDbContextFactory<LocalBoardDbContext> dbFactory,
    IAuthTokenStore tokens,
    RemoteBoardDataService remote,
    IServiceProvider services,
    IUserTimeZoneService timeZone,
    ILogger<LocalFirstBoardDataService> logger)
    : IBoardDataService, IMauiBoardLocalStoreLifecycle
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ClearAllLocalStateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureSqliteBoardColumnsAsync(db, cancellationToken);
            await db.BoardItems.ExecuteDeleteAsync(cancellationToken);
            await db.Outbox.ExecuteDeleteAsync(cancellationToken);
            var meta = await db.Meta.FindAsync([1], cancellationToken);
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
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureSqliteBoardColumnsAsync(db, cancellationToken);

            if (!await HasAuthAsync(cancellationToken))
                return EmptySnapshot();

            var userKey = await ResolveUserKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(userKey))
                return EmptySnapshot();

            await EnsureUserScopeAsync(db, userKey, cancellationToken);
            var snap = ReadSnapshot(db, userKey);
            if (IsEmpty(snap))
            {
                try
                {
                    var fresh = await remote.GetSnapshotAsync(cancellationToken);
                    await ReplaceMirrorAsync(db, userKey, fresh, cancellationToken);
                    snap = ReadSnapshot(db, userKey);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Initial board hydrate from API skipped (offline or error).");
                }
            }

            return snap;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<BoardItem> CreateItemAsync(BoardSection section, string title, Guid? itemId = null,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            cancellationToken,
            async (db, userKey) =>
            {
                var id = itemId ?? Guid.NewGuid();
                var now = DateTimeOffset.UtcNow;
                var sortOrder = await GetNextLocalSortOrderAsync(db, userKey, section, cancellationToken);
                var item = new BoardItem(id, title.Trim(), CreatedAtUtc: now, SortOrder: sortOrder);
                db.BoardItems.Add(LocalBoardItemRow.FromModel(section, userKey, item, true));
                var payload = new CreateOutboxPayload(section, item.Title, id);
                Enqueue(db, userKey, BoardOutboxOperationKind.Create, payload);
                await db.SaveChangesAsync(cancellationToken);
                return item;
            });

    public Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            cancellationToken,
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId,
                    cancellationToken);
                if (row is null) return null;

                var expected = row.ServerUpdatedAtUtc;
                row.Title = title.Trim();
                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.Rename,
                    new RenameOutboxPayload(section, itemId, row.Title, expected));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            });

    public Task<bool> DeleteItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            cancellationToken,
            async (db, userKey) =>
            {
                if (await TryCoalesceDeletePendingCreateAsync(db, userKey, itemId, cancellationToken))
                    return true;

                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId,
                    cancellationToken);
                if (row is null) return false;

                var expected = row.ServerUpdatedAtUtc;
                db.BoardItems.Remove(row);
                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.Delete,
                    new SectionItemOutboxPayload(section, itemId, expected));
                await db.SaveChangesAsync(cancellationToken);
                return true;
            });

    public Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            cancellationToken,
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId,
                    cancellationToken);
                if (row is null) return null;

                var expected = row.ServerUpdatedAtUtc;
                ApplyLocalToggle(section, row);
                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.Toggle,
                    new SectionItemOutboxPayload(section, itemId, expected));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            });

    public Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            cancellationToken,
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && x.Section == BoardSection.Daily,
                    cancellationToken);
                if (row is null) return null;

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
            });

    public Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            cancellationToken,
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && x.Section == BoardSection.Habit,
                    cancellationToken);
                if (row is null) return null;

                var expectedInc = row.ServerUpdatedAtUtc;
                if (row.TrackPlus) row.Counter++;

                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.HabitIncrement,
                    new ItemIdOutboxPayload(itemId, expectedInc));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            });

    public Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            cancellationToken,
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && x.Section == BoardSection.Habit,
                    cancellationToken);
                if (row is null) return null;

                var expectedDec = row.ServerUpdatedAtUtc;
                if (row.TrackMinus) row.NegativeCounter++;

                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.HabitDecrement,
                    new ItemIdOutboxPayload(itemId, expectedDec));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            });

    public Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        bool trackPlus,
        bool trackMinus,
        HabitResetPeriod resetPeriod,
        int counter,
        int negativeCounter,
        string? checklistJson = null,
        double? sortOrder = null,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            cancellationToken,
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && x.Section == BoardSection.Habit,
                    cancellationToken);
                if (row is null) return null;

                var expected = row.ServerUpdatedAtUtc;
                row.Title = title.Trim();
                row.Notes = notes;
                row.Tags = tags;
                row.TrackPlus = trackPlus;
                row.TrackMinus = trackMinus;
                row.ResetPeriod = resetPeriod;
                row.Counter = counter;
                row.NegativeCounter = negativeCounter;
                row.ChecklistJson = checklistJson;
                if (sortOrder.HasValue)
                {
                    row.SortOrder = sortOrder.Value;
                }
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
                        sortOrder));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            });

    public Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        string? checklistJson,
        DateTime? dueDate,
        double? sortOrder = null,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            cancellationToken,
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && x.Section == BoardSection.Todo,
                    cancellationToken);
                if (row is null) return null;

                var expectedTodo = row.ServerUpdatedAtUtc;
                row.Title = title.Trim();
                row.Notes = notes;
                row.Tags = tags;
                row.ChecklistJson = checklistJson;
                row.TodoDueDate = dueDate.HasValue ? DateOnly.FromDateTime(dueDate.Value.Date) : null;
                if (sortOrder.HasValue)
                {
                    row.SortOrder = sortOrder.Value;
                }
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
                        dueDate,
                        expectedTodo,
                        sortOrder));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            });

    public Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        DateTime? startDate,
        DailyRepeatType repeatType,
        int repeatInterval,
        string? checklistJson,
        int streak,
        double? sortOrder = null,
        CancellationToken cancellationToken = default) =>
        MutateWithSyncAsync(
            cancellationToken,
            async (db, userKey) =>
            {
                var row = await db.BoardItems.FirstOrDefaultAsync(
                    x => x.UserKey == userKey && x.Id == itemId && x.Section == BoardSection.Daily,
                    cancellationToken);
                if (row is null) return null;

                var expectedDaily = row.ServerUpdatedAtUtc;
                row.Title = title.Trim();
                row.Notes = notes;
                row.Tags = tags;
                row.DailyStartDate = startDate.HasValue ? DateOnly.FromDateTime(startDate.Value.Date) : null;
                row.DailyRepeat = repeatType;
                row.DailyRepeatInterval = repeatInterval;
                row.ChecklistJson = checklistJson;
                row.Counter = streak;
                if (sortOrder.HasValue)
                {
                    row.SortOrder = sortOrder.Value;
                }
                Enqueue(
                    db,
                    userKey,
                    BoardOutboxOperationKind.UpdateDaily,
                    new UpdateDailyOutboxPayload(
                        itemId,
                        row.Title,
                        row.Notes,
                        row.Tags,
                        startDate,
                        repeatType,
                        repeatInterval,
                        row.ChecklistJson,
                        streak,
                        expectedDaily,
                        sortOrder));
                await db.SaveChangesAsync(cancellationToken);
                return row.ToModel();
            });

    /// <summary>Runs at most one outbox HTTP operation. Returns true if an entry was processed successfully.</summary>
    public async Task<bool> TryDrainOneOutboxOperationAsync(CancellationToken cancellationToken = default)
    {
        Guid operationId;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureSqliteBoardColumnsAsync(db, cancellationToken);
            if (!await HasAuthAsync(cancellationToken)) return false;

            var userKey = await ResolveUserKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(userKey)) return false;

            await EnsureUserScopeAsync(db, userKey, cancellationToken);

            var head = await db.Outbox
                .Where(o => o.UserKey == userKey)
                .OrderBy(o => o.CreatedAtUtc)
                .ThenBy(o => o.OperationId)
                .FirstOrDefaultAsync(cancellationToken);

            if (head is null) return false;

            if (head.AttemptCount > 0 && head.LastAttemptUtc is { } last)
            {
                var wait = Backoff(head.AttemptCount);
                if (DateTime.UtcNow < last + wait) return false;
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
                if (still is not null) db.Outbox.Remove(still);

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
            logger.LogWarning(ex, "Outbox operation {OperationId} returned 409; dropping op and requesting resync.", operationId);
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

            RequestSyncSoon();
            return false;
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

    public async Task<string?> TryGetStuckOutboxHintAsync(int minAttempts, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureSqliteBoardColumnsAsync(db, cancellationToken);
            if (!await HasAuthAsync(cancellationToken)) return null;

            var userKey = await ResolveUserKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(userKey)) return null;

            var row = await db.Outbox
                .Where(o => o.UserKey == userKey && o.AttemptCount >= minAttempts)
                .OrderByDescending(o => o.AttemptCount)
                .FirstOrDefaultAsync(cancellationToken);

            if (row is null) return null;

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
        if (string.IsNullOrWhiteSpace(userKey) || !await HasAuthAsync(cancellationToken)) return false;

        string? cursor;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureSqliteBoardColumnsAsync(db, cancellationToken);
            await EnsureUserScopeAsync(db, userKey, cancellationToken);

            if (await db.Outbox.AnyAsync(o => o.UserKey == userKey, cancellationToken)) return false;

            var metaRow = await db.Meta.FindAsync([1], cancellationToken);
            cursor = metaRow?.LastSyncCursorUtc;
        }
        finally
        {
            _gate.Release();
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            BoardSyncDelta? delta;
            try
            {
                delta = await remote.TryGetSyncDeltaAsync(cursor!, cancellationToken);
            }
            catch
            {
                return false;
            }

            if (delta is null)
            {
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                    var metaRow = await db.Meta.FindAsync([1], cancellationToken);
                    if (metaRow is not null)
                    {
                        metaRow.LastSyncCursorUtc = null;
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
            else
            {
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                    if (await db.Outbox.AnyAsync(o => o.UserKey == userKey, cancellationToken)) return false;

                    var pending = await db.Outbox
                        .Where(o => o.UserKey == userKey)
                        .Select(o => new { o.Kind, o.PayloadJson })
                        .ToListAsync(cancellationToken);
                    var skipIds = BoardOutboxReferencedIds.CollectFromPayloads(
                        pending.Select(p => (p.Kind, p.PayloadJson)));

                    await ApplySyncDeltaAsync(db, userKey, delta, skipIds, cancellationToken);

                    var metaRow = await db.Meta.FindAsync([1], cancellationToken);
                    if (metaRow is not null) metaRow.LastSyncCursorUtc = delta.NextCursor;

                    await db.SaveChangesAsync(cancellationToken);
                    return true;
                }
                finally
                {
                    _gate.Release();
                }
            }
        }

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
            if (await db.Outbox.AnyAsync(o => o.UserKey == userKey, cancellationToken)) return false;

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
                    p.Title,
                    p.Notes,
                    p.Tags,
                    p.TrackPlus,
                    p.TrackMinus,
                    p.ResetPeriod,
                    p.Counter,
                    p.NegativeCounter,
                    p.ChecklistJson,
                    p.SortOrder,
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
                    p.Title,
                    p.Notes,
                    p.Tags,
                    p.ChecklistJson,
                    p.DueDate,
                    p.SortOrder,
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
                    p.Title,
                    p.Notes,
                    p.Tags,
                    p.StartDate,
                    p.RepeatType,
                    p.RepeatInterval,
                    p.ChecklistJson,
                    p.Streak,
                    p.SortOrder,
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

    private async Task CommitCreateSuccessAsync(Guid clientId, BoardSection section, BoardItem serverItem, string userKey,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var old = await db.BoardItems.FindAsync([clientId], cancellationToken);
            if (old is not null) db.BoardItems.Remove(old);

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
        if (serverItem is null) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.BoardItems.FirstOrDefaultAsync(
                x => x.UserKey == userKey && x.Id == itemId,
                cancellationToken);
            if (row is null) return;

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

    private async Task<T> MutateAsync<T>(CancellationToken cancellationToken, Func<LocalBoardDbContext, string, Task<T>> action)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureSqliteBoardColumnsAsync(db, cancellationToken);

            if (!await HasAuthAsync(cancellationToken))
                throw new InvalidOperationException("Sign in to change your board.");

            var userKey = await ResolveUserKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(userKey))
                throw new InvalidOperationException("Sign in to change your board.");

            await EnsureUserScopeAsync(db, userKey, cancellationToken);
            return await action(db, userKey);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> MutateWithSyncAsync<T>(CancellationToken cancellationToken,
        Func<LocalBoardDbContext, string, Task<T>> action)
    {
        try
        {
            return await MutateAsync(cancellationToken, action);
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

    private async Task<bool> TryCoalesceDeletePendingCreateAsync(LocalBoardDbContext db, string userKey, Guid itemId,
        CancellationToken cancellationToken)
    {
        var pending = await db.Outbox
            .Where(o => o.UserKey == userKey && o.Kind == BoardOutboxOperationKind.Create)
            .ToListAsync(cancellationToken);

        foreach (var row in pending)
        {
            var p = System.Text.Json.JsonSerializer.Deserialize<CreateOutboxPayload>(row.PayloadJson, BoardOutboxJson.Options);
            if (p?.ClientItemId != itemId) continue;

            db.Outbox.Remove(row);
            var entity = await db.BoardItems.FindAsync([itemId], cancellationToken);
            if (entity is not null) db.BoardItems.Remove(entity);

            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    private async Task EnsureUserScopeAsync(LocalBoardDbContext db, string userKey, CancellationToken cancellationToken)
    {
        var meta = await db.Meta.FindAsync([1], cancellationToken);
        if (meta is null)
        {
            db.Meta.Add(new LocalBoardStoreMetaRow { Id = 1, BoundUserKey = userKey });
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (string.Equals(meta.BoundUserKey, userKey, StringComparison.Ordinal)) return;

        await db.BoardItems.ExecuteDeleteAsync(cancellationToken);
        await db.Outbox.ExecuteDeleteAsync(cancellationToken);
        meta.BoundUserKey = userKey;
        meta.LastSyncCursorUtc = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> ResolveUserKeyAsync(CancellationToken cancellationToken)
    {
        var email = await tokens.GetEmailAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(email)) return email.Trim().ToLowerInvariant();

        var jwt = await tokens.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(jwt)) return null;

        var fromJwt = JwtAccessTokenDisplayClaims.TryGetEmail(jwt);
        return string.IsNullOrWhiteSpace(fromJwt) ? null : fromJwt.Trim().ToLowerInvariant();
    }

    private async Task<bool> HasAuthAsync(CancellationToken cancellationToken) =>
        !string.IsNullOrEmpty(await tokens.GetAccessTokenAsync(cancellationToken));

    private BoardSnapshot ReadSnapshot(LocalBoardDbContext db, string userKey)
    {
        var today = DailySchedule.LocalToday(timeZone);
        var items = db.BoardItems.AsNoTracking().Where(x => x.UserKey == userKey).ToList();
        // Match BoardPersistenceService.GetSnapshotAsync ordering (web app).
        var habits = items.Where(x => x.Section == BoardSection.Habit)
            .OrderBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Id)
            .Select(x => x.ToModel())
            .ToList();
        var dailies = items.Where(x => x.Section == BoardSection.Daily)
            .OrderBy(x => IsDailyRowCompleteForToday(x, today) ? 1 : 0)
            .ThenBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Id)
            .Select(x => x.ToModel())
            .ToList();
        var todos = items.Where(x => x.Section == BoardSection.Todo)
            .OrderBy(x => x.IsCompleted ? 1 : 0)
            .ThenBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Id)
            .Select(x => x.ToModel())
            .ToList();
        return new BoardSnapshot(habits, dailies, todos);
    }

    private static bool IsDailyRowCompleteForToday(LocalBoardItemRow row, DateOnly today)
    {
        if (row.DailyLastCompletedOn is { } t && t == today) return true;
        return row.DailyLastCompletedOn is null && row.IsCompleted;
    }

    private static async Task ReplaceMirrorAsync(LocalBoardDbContext db, string userKey, BoardSnapshot snap,
        CancellationToken cancellationToken)
    {
        await db.BoardItems.Where(x => x.UserKey == userKey).ExecuteDeleteAsync(cancellationToken);
        foreach (var h in snap.Habits)
            db.BoardItems.Add(LocalBoardItemRow.FromModel(BoardSection.Habit, userKey, h, false));

        foreach (var d in snap.Dailies)
            db.BoardItems.Add(LocalBoardItemRow.FromModel(BoardSection.Daily, userKey, d, false));

        foreach (var t in snap.Todos)
            db.BoardItems.Add(LocalBoardItemRow.FromModel(BoardSection.Todo, userKey, t, false));

        await db.SaveChangesAsync(cancellationToken);

        var meta = await db.Meta.FindAsync([1], cancellationToken);
        if (meta is not null) meta.LastSyncCursorUtc = ComputeMirrorCursor(snap);

        await db.SaveChangesAsync(cancellationToken);
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
                m = m is null || u > m ? u : m;
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
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE BoardItems ADD COLUMN SortOrder REAL NULL;",
                cancellationToken);
        }
        catch
        {
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Meta ADD COLUMN LastSyncCursorUtc TEXT NULL;",
                cancellationToken);
        }
        catch
        {
        }
    }

    private static async Task ApplySyncDeltaAsync(
        LocalBoardDbContext db,
        string userKey,
        BoardSyncDelta delta,
        HashSet<Guid> skipIds,
        CancellationToken cancellationToken)
    {
        var syncIds = delta.Items
            .Select(x => x.Item.Id)
            .Where(id => !skipIds.Contains(id))
            .ToList();
        var existingRows = syncIds.Count == 0
            ? new Dictionary<Guid, LocalBoardItemRow>()
            : await db.BoardItems
                .Where(x => x.UserKey == userKey && syncIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var id in delta.DeletedItemIds)
        {
            if (skipIds.Contains(id)) continue;
            await db.BoardItems.Where(x => x.UserKey == userKey && x.Id == id).ExecuteDeleteAsync(cancellationToken);
        }

        foreach (var entry in delta.Items)
        {
            if (skipIds.Contains(entry.Item.Id)) continue;
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

    private static async Task<double> GetNextLocalSortOrderAsync(
        LocalBoardDbContext db,
        string userKey,
        BoardSection section,
        CancellationToken cancellationToken)
    {
        var max = await db.BoardItems
            .Where(x => x.UserKey == userKey && x.Section == section)
            .Select(x => x.SortOrder)
            .MaxAsync(cancellationToken);
        return (max ?? 0) + 1.0;
    }
}
