using App.Shared.RCL.Models;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services.Board.Local;

public sealed partial class LocalFirstBoardDataService
{
    public async Task<bool> TryPullRemoteMirrorAsync(CancellationToken cancellationToken = default)
    {
        var userKey = await users.GetUserKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(userKey) || !await users.HasAuthAsync(cancellationToken))
        {
            return false;
        }

        await store.EnsureReadyAsync(cancellationToken);

        string? cursor;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureUserScopeAsync(userKey, cancellationToken);
            cursor = (await store.GetMetaAsync(cancellationToken)).LastSyncCursorUtc;
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Sync delta pull failed; falling back to a full snapshot.");
            return (true, false);
        }

        if (delta is null)
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
        foreach (var id in delta.DeletedItemIds)
        {
            if (skipIds.Contains(id))
            {
                continue;
            }

            await store.DeleteItemAsync(userKey, id, cancellationToken);
        }

        foreach (var entry in delta.Items)
        {
            if (skipIds.Contains(entry.Item.Id))
            {
                continue;
            }

            var existing = await store.FindItemAsync(userKey, entry.Item.Id, cancellationToken);
            var awaiting = existing?.AwaitingServerCreate ?? false;
            await store.UpsertItemAsync(BoardLocalRow.FromModel(entry.Section, userKey, entry.Item, awaiting), cancellationToken);
        }
    }
}
