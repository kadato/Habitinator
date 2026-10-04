using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Remote;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services.Board.Local;

/// <summary>Shared local-first board. Reads and writes go to <see cref="IBoardLocalStore" /> with no network on the hot path. <see cref="BoardSyncCoordinator" /> moves queued writes and pulls remote changes.</summary>
#pragma warning disable CA1001 // DI singleton: owns a long-lived SemaphoreSlim and is never disposed by the container.
public sealed partial class LocalFirstBoardDataService(
    IBoardLocalStore store,
    ICurrentUserKeyProvider users,
    RemoteBoardDataService remote,
    IServiceProvider services,
    IUserTimeZoneService timeZone,
    BoardSyncStatus syncStatus,
    ILogger<LocalFirstBoardDataService> logger)
    : IBoardDataService, IBoardLocalStoreLifecycle
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Operations currently on the wire. Guarded by <see cref="_gate" />. Used to keep delete coalescing away from creates that may already have reached the server.</summary>
    private readonly HashSet<Guid> _inFlightOutboxOperations = [];

    /// <summary>Last snapshot read from the local store. Serves the synchronous fast path in the board component. Cleared on every mutation and user switch.</summary>
    private volatile BoardSnapshot? _cachedSnapshot;

    private async Task<DateOnly> TodayAsync(CancellationToken cancellationToken)
    {
        var prefs = await services
            .GetRequiredService<IUserPreferencesService>()
            .GetAsync(cancellationToken);
        return DailySchedule.LocalToday(timeZone, prefs.DayStartLocalTime);
    }

    public Task EnsureStoreReadyAsync(CancellationToken cancellationToken = default) =>
        store.EnsureReadyAsync(cancellationToken);

    public async Task ClearAllLocalStateAsync(CancellationToken cancellationToken = default)
    {
        await store.EnsureReadyAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _cachedSnapshot = null;
            await store.ClearAllStateAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        await store.FlushAsync(cancellationToken);
    }

    public bool TryGetCachedSnapshot(out BoardSnapshot? snapshot)
    {
        snapshot = _cachedSnapshot;
        return snapshot is not null;
    }

    public async Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await store.EnsureReadyAsync(cancellationToken);

        string? userKey;
        BoardSnapshot snap;
        var shouldFetchRemote = false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            userKey = await ResolveAuthedUserKeyAsync(cancellationToken);
            if (userKey is null)
            {
                return EmptySnapshot();
            }

            await EnsureUserScopeAsync(userKey, cancellationToken);
            var today = await TodayAsync(cancellationToken);
            snap = ReadSnapshot(await store.ListItemsAsync(userKey, includeArchived: false, cancellationToken), today);
            _cachedSnapshot = snap;

            if (IsEmpty(snap) && !syncStatus.IsSyncing)
            {
                shouldFetchRemote = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (shouldFetchRemote && userKey is not null)
        {
            snap = await TryFetchAndReplaceIfEmptyAsync(userKey, cancellationToken) ?? snap;
        }

        return snap;
    }

    private async Task<BoardSnapshot?> TryFetchAndReplaceIfEmptyAsync(string userKey, CancellationToken cancellationToken)
    {
        BoardSnapshot? fresh = null;
        try
        {
            fresh = await remote.GetSnapshotAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Initial board hydrate from API skipped. Offline or error.");
        }

        if (fresh is null)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureUserScopeAsync(userKey, cancellationToken);
            var today = await TodayAsync(cancellationToken);
            var current = ReadSnapshot(await store.ListItemsAsync(userKey, includeArchived: false, cancellationToken), today);
            if (!IsEmpty(current))
            {
                _cachedSnapshot = current;
                return current;
            }

            await ReplaceMirrorAsync(userKey, fresh, cancellationToken);
            var replaced = ReadSnapshot(await store.ListItemsAsync(userKey, includeArchived: false, cancellationToken), today);
            _cachedSnapshot = replaced;
            return replaced;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BoardItem?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await store.EnsureReadyAsync(cancellationToken);

        var row = await FindScopedRowAsync(itemId, cancellationToken);
        if (row is null)
        {
            // Fresh scopes start with an empty mirror. Pull once under the same rule as GetSnapshotAsync.
            var userKey = await ResolveAuthedUserKeyAsync(cancellationToken);
            if (userKey is not null)
            {
                await TryFetchAndReplaceIfEmptyAsync(userKey, cancellationToken);
                row = await FindScopedRowAsync(itemId, cancellationToken);
            }
        }

        if (row is null || row.IsArchived)
        {
            return null;
        }

        if (row.Section == BoardSection.Habit)
        {
            var today = await TodayAsync(cancellationToken);
            return ToEffectiveModel(row, today);
        }

        return row.ToModel();
    }

    private async Task<BoardLocalRow?> FindScopedRowAsync(Guid itemId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userKey = await ResolveAuthedUserKeyAsync(cancellationToken);
            if (userKey is null)
            {
                return null;
            }

            await EnsureUserScopeAsync(userKey, cancellationToken);
            return await store.FindItemAsync(userKey, itemId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Dictionary<Guid, int>> GetStreakMapAsync(CancellationToken cancellationToken = default)
    {
        await store.EnsureReadyAsync(cancellationToken);
        var userKey = await users.GetUserKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(userKey))
        {
            return [];
        }

        // The server owns streak math. A missed day resets the streak
        // without touching any board row. The local mirror then keeps
        // the old Counter until a full snapshot replaces it. Use the
        // remote map when online so the board keeps the correct
        // prerendered snapshot instead of flipping back to stale values.
        Dictionary<Guid, int>? remoteMap = null;
        try
        {
            remoteMap = await remote.GetStreakMapAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Remote streak map unavailable. Use local counters.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await store.ListItemsAsync(userKey, includeArchived: false, cancellationToken);
            if (remoteMap is null)
            {
                return items
                    .Where(x => x.Section == BoardSection.Daily)
                    .ToDictionary(x => x.Id, x => x.Counter);
            }

            // Pending outbox ops mean the server has not seen our latest optimistic
            // streak yet. Keep the local Counter for those items so a retro check-in
            // does not flicker back to the pre-sync value.
            var pendingIds = new HashSet<Guid>();
            try
            {
                var pending = await store.ListOutboxAsync(userKey, cancellationToken);
                pendingIds = BoardOutboxReferencedIds.CollectFromPayloads(
                    pending.Select(p => (p.Kind, p.PayloadJson)));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not list outbox for streak merge.");
            }

            var merged = new Dictionary<Guid, int>(remoteMap.Count);
            var changed = false;
            foreach (var row in items.Where(x => x.Section == BoardSection.Daily))
            {
                if (pendingIds.Contains(row.Id))
                {
                    merged[row.Id] = row.Counter;
                    continue;
                }

                if (remoteMap.TryGetValue(row.Id, out var streak))
                {
                    merged[row.Id] = streak;
                    if (row.Counter != streak)
                    {
                        row.Counter = streak;
                        await store.UpsertItemAsync(row, cancellationToken);
                        changed = true;
                    }
                }
                else
                {
                    merged[row.Id] = row.Counter;
                }
            }

            foreach (var kvp in remoteMap.Where(kvp => !merged.ContainsKey(kvp.Key)))
            {
                merged[kvp.Key] = kvp.Value;
            }

            if (changed)
            {
                _cachedSnapshot = null;
            }

            return merged;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureUserScopeAsync(string userKey, CancellationToken cancellationToken)
    {
        var meta = await store.GetMetaAsync(cancellationToken);
        if (meta.BoundUserKey is null)
        {
            await store.SetMetaAsync(new BoardStoreMeta { BoundUserKey = userKey, LastSyncCursorUtc = meta.LastSyncCursorUtc }, cancellationToken);
            return;
        }

        if (string.Equals(meta.BoundUserKey, userKey, StringComparison.Ordinal))
        {
            return;
        }

        _cachedSnapshot = null;
        await store.DeleteAllItemsAsync(meta.BoundUserKey, cancellationToken);
        await store.ClearOutboxForUserAsync(meta.BoundUserKey, cancellationToken);
        await store.SetMetaAsync(new BoardStoreMeta { BoundUserKey = userKey }, cancellationToken);
    }

    private async Task<string?> ResolveAuthedUserKeyAsync(CancellationToken cancellationToken)
    {
        if (!await users.HasAuthAsync(cancellationToken))
        {
            return null;
        }

        var userKey = await users.GetUserKeyAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(userKey) ? null : userKey;
    }

    private static BoardSnapshot ReadSnapshot(IReadOnlyList<BoardLocalRow> items, DateOnly today)
    {
        var (habits, dailies, todos) = OrderRows(items, today);
        return new(habits, dailies, todos);
    }

    private static (List<BoardItem> Habits, List<BoardItem> Dailies, List<BoardItem> Todos) OrderRows(
        IReadOnlyList<BoardLocalRow> items, DateOnly today)
    {
        List<BoardItem> habits = [.. BoardOrdering.SortHabits(
            items.Where(x => x.Section == BoardSection.Habit),
            x => x.SortOrder ?? double.MaxValue,
            x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue,
            x => x.Id)
            .Select(x => ToEffectiveModel(x, today))];
        List<BoardItem> dailies = [.. BoardOrdering.SortDailies(
            items.Where(x => x.Section == BoardSection.Daily),
            x => IsDailyRowCompleteForToday(x, today),
            x => x.SortOrder ?? double.MaxValue,
            x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue,
            x => x.Id)
            .Select(x => x.ToModel())];
        List<BoardItem> todos = [.. BoardOrdering.SortTodos(
            items.Where(x => x.Section == BoardSection.Todo),
            x => x.IsCompleted,
            x => x.TodoDueDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            x => x.SortOrder ?? double.MaxValue,
            x => x.CreatedAtUtc ?? DateTimeOffset.MaxValue,
            x => x.Id)
            .Select(x => x.ToModel())];
        return (habits, dailies, todos);
    }

    internal static BoardItem ToEffectiveModel(BoardLocalRow row, DateOnly today) =>
        BoardItemMapper.WithLocalDay(row.ToModel(), row.Section, today);

    internal static void EnsureLocalHabitPeriodCurrent(BoardLocalRow row, DateOnly today)
    {
        if (row.Section != BoardSection.Habit)
        {
            return;
        }

        var current = HabitResetSchedule.PeriodStartFor(today, row.ResetPeriod);
        if (row.HabitPeriodStart is null)
        {
            row.HabitPeriodStart = current;
            return;
        }

        if (HabitResetSchedule.NeedsReset(row.HabitPeriodStart, today, row.ResetPeriod))
        {
            row.Counter = 0;
            row.NegativeCounter = 0;
            row.HabitPeriodStart = current;
        }
    }

    private static bool IsDailyRowCompleteForToday(BoardLocalRow row, DateOnly today) =>
        DailySchedule.IsCompletedForToday(row.DailyLastCompletedOn, row.IsCompleted, today);

    private static bool IsEmpty(BoardSnapshot s) =>
        s.Habits.Count == 0 && s.Dailies.Count == 0 && s.Todos.Count == 0;

    private static BoardSnapshot EmptySnapshot() => new([], [], []);
}
#pragma warning restore CA1001
