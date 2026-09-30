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
