using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Remote;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services.Board.Local;

public sealed partial class LocalFirstBoardDataService
{
    /// <summary>How long a full snapshot mirror stays fresh. Older mirrors fall back to a full
    /// pull instead of a delta, so rows deleted without tombstones cannot linger as ghosts.</summary>
    private static readonly TimeSpan FullMirrorMaxAge = TimeSpan.FromHours(24);

    public async Task<bool> TryPullRemoteMirrorAsync(CancellationToken cancellationToken = default)
    {
        var userKey = await users.GetUserKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(userKey) || !await users.HasAuthAsync(cancellationToken))
        {
            return false;
        }

        await store.EnsureReadyAsync(cancellationToken);

        string? cursor;
        bool fullMirrorStale;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureUserScopeAsync(userKey, cancellationToken);
            var meta = await store.GetMetaAsync(cancellationToken);
            cursor = meta.LastSyncCursorUtc;
            fullMirrorStale = meta.LastFullMirrorUtc is not { } stamped
                || DateTimeOffset.UtcNow - stamped >= FullMirrorMaxAge;
        }
        finally
        {
            _gate.Release();
        }

        if (!string.IsNullOrWhiteSpace(cursor) && !fullMirrorStale)
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
        // Pull paged deltas and merge them before applying so a large backlog
        // never arrives as one huge payload. Pages share one apply so skip-id
        // filtering and the stored cursor stay consistent.
        var items = new List<BoardSyncItem>();
        var deleted = new List<Guid>();
        var finalCursor = cursor;
        string? pageCursor = cursor;

        for (var page = 0; page < RemoteBoardDataService.SyncMaxPages; page++)
        {
            BoardSyncDelta? pageDelta;
            try
            {
                pageDelta = await remote.TryGetSyncDeltaAsync(pageCursor, RemoteBoardDataService.SyncPageSize, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Sync delta pull failed; falling back to a full snapshot.");
                return (true, false);
            }

            if (pageDelta is null)
            {
                if (page == 0)
                {
                    await _gate.WaitAsync(cancellationToken);
                    try
                    {
                        var meta = await store.GetMetaAsync(cancellationToken);
                        meta.LastSyncCursorUtc = null;
                        await store.SetMetaAsync(meta, cancellationToken);
                    }
                    finally
                    {
                        _gate.Release();
                    }

                    return (false, false);
                }

                break;
            }

            items.AddRange(pageDelta.Items);
            deleted.AddRange(pageDelta.DeletedItemIds);
            finalCursor = pageDelta.NextCursor;
            if (pageDelta.Items.Count + pageDelta.DeletedItemIds.Count < RemoteBoardDataService.SyncPageSize)
            {
                break;
            }

            pageCursor = pageDelta.NextCursor;
        }

        var delta = new BoardSyncDelta(items, deleted, finalCursor);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var pending = await store.ListOutboxAsync(userKey, cancellationToken);
            var skipIds = BoardOutboxReferencedIds.CollectFromPayloads(
                pending.Select(p => (p.Kind, p.PayloadJson)));

            await ApplySyncDeltaAsync(userKey, delta, skipIds, cancellationToken);

            var meta = await store.GetMetaAsync(cancellationToken);
            meta.LastSyncCursorUtc = delta.NextCursor;
            await store.SetMetaAsync(meta, cancellationToken);
            _cachedSnapshot = null;
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Sync snapshot pull failed (offline or error).");
            return false;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (await store.HasOutboxAsync(userKey, cancellationToken))
            {
                return false;
            }

            await ReplaceMirrorAsync(userKey, snap, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReplaceMirrorAsync(string userKey, BoardSnapshot snap, CancellationToken cancellationToken)
    {
        List<BoardLocalRow> rows = [
            .. snap.Habits.Select(h => BoardLocalRow.FromModel(BoardSection.Habit, userKey, h, false)),
            .. snap.Dailies.Select(d => BoardLocalRow.FromModel(BoardSection.Daily, userKey, d, false)),
            .. snap.Todos.Select(t => BoardLocalRow.FromModel(BoardSection.Todo, userKey, t, false))];
        await store.ReplaceAllItemsAsync(userKey, rows, ComputeMirrorCursor(snap), cancellationToken);
        var meta = await store.GetMetaAsync(cancellationToken);
        meta.LastFullMirrorUtc = DateTimeOffset.UtcNow;
        await store.SetMetaAsync(meta, cancellationToken);
        _cachedSnapshot = null;
    }

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

    private async Task ApplySyncDeltaAsync(
        string userKey,
        BoardSyncDelta delta,
        HashSet<Guid> skipIds,
        CancellationToken cancellationToken)
    {
        var deletedIds = delta.DeletedItemIds
            .Where(id => !skipIds.Contains(id))
            .ToList();
        var upserts = delta.Items
            .Where(entry => !skipIds.Contains(entry.Item.Id))
            .Select(entry => (entry.Section, entry.Item))
            .ToList();

        await store.ApplyDeltaAsync(userKey, upserts, deletedIds, cancellationToken);
    }
}
