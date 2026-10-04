namespace App.Shared.RCL.Services.Board.Local;

public interface ICurrentUserKeyProvider
{
    Task<string?> GetUserKeyAsync(CancellationToken cancellationToken = default);

    Task<bool> HasAuthAsync(CancellationToken cancellationToken = default);
}

public interface IBoardSyncRequestor
{
    void RequestSync(bool notifyOnProgress = true);

    /// <summary>Drains pending operations and pulls the mirror now, so reads after a mutation see the mirror.</summary>
    Task SyncNowAsync(CancellationToken cancellationToken = default);
}

public interface IBoardLocalStoreLifecycle
{
    Task EnsureStoreReadyAsync(CancellationToken cancellationToken = default);

    Task ClearAllLocalStateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Server prerender has no local store, so the lifecycle methods are no-ops.</summary>
public sealed class NoOpBoardLocalStoreLifecycle : IBoardLocalStoreLifecycle
{
    public Task EnsureStoreReadyAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ClearAllLocalStateAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>Server prerender has no outbox, so sync requests are no-ops.</summary>
public sealed class NoOpBoardSyncRequestor : IBoardSyncRequestor
{
    public void RequestSync(bool notifyOnProgress = true)
    {
    }

    public Task SyncNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
