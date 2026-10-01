using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services.Board.Local;

public interface IBoardLocalStore
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);

    Task<BoardStoreMeta> GetMetaAsync(CancellationToken cancellationToken = default);

    Task SetMetaAsync(BoardStoreMeta meta, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BoardLocalRow>> ListItemsAsync(string userKey, bool includeArchived, CancellationToken cancellationToken = default);

    Task<BoardLocalRow?> FindItemAsync(string userKey, Guid id, CancellationToken cancellationToken = default);

    Task UpsertItemAsync(BoardLocalRow row, CancellationToken cancellationToken = default);

    Task DeleteItemAsync(string userKey, Guid id, CancellationToken cancellationToken = default);

    Task DeleteAllItemsAsync(string userKey, CancellationToken cancellationToken = default);

    Task ReplaceAllItemsAsync(string userKey, IReadOnlyList<BoardLocalRow> rows, string? cursor, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Applies a sync delta to the mirror: delete the listed ids, then upsert the listed items,
    ///     preserving <see cref="BoardLocalRow.AwaitingServerCreate" /> on rows that already exist.
    ///     Stores that can batch should override this. The default loops the single-item methods.
    /// </summary>
    async Task ApplyDeltaAsync(
        string userKey,
        IReadOnlyList<(BoardSection Section, BoardItem Item)> upserts,
        IReadOnlyList<Guid> deletedIds,
        CancellationToken cancellationToken = default)
    {
        foreach (var id in deletedIds)
        {
            await DeleteItemAsync(userKey, id, cancellationToken);
        }

        foreach (var (section, item) in upserts)
        {
            var existing = await FindItemAsync(userKey, item.Id, cancellationToken);
            var awaiting = existing?.AwaitingServerCreate ?? false;
            await UpsertItemAsync(BoardLocalRow.FromModel(section, userKey, item, awaiting), cancellationToken);
        }
    }

    Task<IReadOnlyList<BoardOutboxEntry>> ListOutboxAsync(string userKey, CancellationToken cancellationToken = default);

    Task<BoardOutboxEntry?> FindOutboxAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task EnqueueOutboxAsync(BoardOutboxEntry entry, CancellationToken cancellationToken = default);

    Task UpdateOutboxAsync(BoardOutboxEntry entry, CancellationToken cancellationToken = default);

    Task DeleteOutboxAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task ClearOutboxForUserAsync(string userKey, CancellationToken cancellationToken = default);

    Task<bool> HasOutboxAsync(string userKey, CancellationToken cancellationToken = default);

    /// <summary>Wipes every cached row, outbox entry, and cursor. Logout and account deletion call it.</summary>
    Task ClearAllStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists buffered changes. Stores that write through do nothing.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
