using App.MAUI.Data;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.MAUI.Services.LocalBoard;

public sealed partial class LocalFirstBoardDataService
{
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
            row.CopyFrom(upd);
        }
    }


}
