namespace App.Shared.RCL.Services.Board.Local;

public interface ICurrentUserKeyProvider
{
    Task<string?> GetUserKeyAsync(CancellationToken cancellationToken = default);

    Task<bool> HasAuthAsync(CancellationToken cancellationToken = default);
}

public interface IBoardSyncRequestor
{
    void RequestSync(bool notifyOnProgress = true);
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
