using App.Shared.RCL.Services.Board.Local;

namespace App.Shared.RCL.Services;

/// <summary>Pulls server state into the local-first mirror before notifying Blazor to reload.</summary>
public sealed class SyncBeforeNotifyRefreshService(
    RemoteBoardRefreshService core,
    BoardSyncCoordinator coordinator) : IRemoteBoardRefreshService
{
    public void RegisterForRemoteRefresh(Func<Task> onRefresh) => core.RegisterForRemoteRefresh(onRefresh);

    public void UnregisterForRemoteRefresh(Func<Task> onRefresh) => core.UnregisterForRemoteRefresh(onRefresh);

    public async Task NotifyFromRemoteAsync(CancellationToken cancellationToken = default)
    {
        await coordinator.RunPullAndDrainAsync(notifyOnProgress: false, cancellationToken);
        await core.NotifyFromRemoteAsync(cancellationToken);
    }
}
