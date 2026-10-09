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

        // Local-first: never touch the network here. The board renders the
        // SQLite mirror immediately and BoardSyncCoordinator pulls remote
        // changes in the background after InitialBoardLoad completes.
        // Prefs read stays outside the gate so sync work never blocks it.
        var today = await TodayAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userKey = await ResolveAuthedUserKeyAsync(cancellationToken);
            if (userKey is null)
            {
                return EmptySnapshot();
            }

            await EnsureUserScopeAsync(userKey, cancellationToken);
            var snap = ReadSnapshot(await store.ListItemsAsync(userKey, includeArchived: false, cancellationToken), today);
            _cachedSnapshot = snap;
            return snap;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Only GetItemAsync uses this now (deep links into a fresh mirror).
    // GetSnapshotAsync stays local-only so first paint never waits on network.
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

        var today = await TodayAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureUserScopeAsync(userKey, cancellationToken);
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
        // Remote fetch runs alongside the local list so slow networks cost
        // max(remote, local) instead of the sum. Callers invoke this off the
        // critical path (post-paint refresh and warmup).
        var remoteTask = TryGetRemoteStreakMapAsync(cancellationToken);
        var pendingTask = CollectPendingStreakIdsAsync(userKey, cancellationToken);

        IReadOnlyList<BoardLocalRow> items;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            items = await store.ListItemsAsync(userKey, includeArchived: false, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        var remoteMap = await remoteTask;
        if (remoteMap is null)
        {
            return BuildLocalStreakMap(items);
        }

        var pendingIds = await pendingTask;
        var (merged, changedRows) = ComputeMergedStreakMap(items, remoteMap, pendingIds);
        if (changedRows.Count > 0)
        {
            // Counter corrections hit SQLite off the return path. The merged
            // values are already in hand, so N per-row writes never delay UI.
            _ = PersistStreakCorrectionsAsync(changedRows);
        }

        return merged;
    }

    private async Task<Dictionary<Guid, int>?> TryGetRemoteStreakMapAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await remote.GetStreakMapAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Remote streak map unavailable. Use local counters.");
            return null;
        }
    }

    private static Dictionary<Guid, int> BuildLocalStreakMap(IReadOnlyList<BoardLocalRow> items) =>
        items
            .Where(x => x.Section == BoardSection.Daily)
            .ToDictionary(x => x.Id, x => x.Counter);

    private async Task<HashSet<Guid>> CollectPendingStreakIdsAsync(string userKey, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await store.ListOutboxAsync(userKey, cancellationToken);
            return BoardOutboxReferencedIds.CollectFromPayloads(
                pending.Select(p => (p.Kind, p.PayloadJson)));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not list outbox for streak merge.");
            return [];
        }
    }

    private static (Dictionary<Guid, int> Merged, List<BoardLocalRow> Changed) ComputeMergedStreakMap(
        IReadOnlyList<BoardLocalRow> items,
        Dictionary<Guid, int> remoteMap,
        HashSet<Guid> pendingIds)
    {
        // Pending outbox ops mean the server has not seen our latest optimistic
        // streak yet. Keep the local Counter for those items so a retro check-in
        // does not flicker back to the pre-sync value.
        var merged = new Dictionary<Guid, int>(remoteMap.Count);
        List<BoardLocalRow> changed = [];
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
                    changed.Add(row);
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

        return (merged, changed);
    }

    private async Task PersistStreakCorrectionsAsync(List<BoardLocalRow> changedRows)
    {
        try
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                foreach (var row in changedRows)
                {
                    await store.UpsertItemAsync(row, CancellationToken.None);
                }

                _cachedSnapshot = null;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Background streak correction skipped.");
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
