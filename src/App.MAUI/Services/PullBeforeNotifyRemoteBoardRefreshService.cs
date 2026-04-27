using App.Shared.RCL.Services;

namespace App.MAUI.Services;

/// <summary>MAUI: pull server snapshot into SQLite before notifying Blazor to reload from <see cref="IBoardDataService" />.</summary>
public sealed class PullBeforeNotifyRemoteBoardRefreshService : IRemoteBoardRefreshService
{
    private readonly MauiBoardSyncCoordinator _coordinator;
    private readonly RemoteBoardRefreshService _core;

    public PullBeforeNotifyRemoteBoardRefreshService(
        RemoteBoardRefreshService core,
        MauiBoardSyncCoordinator coordinator)
    {
        _core = core;
        _coordinator = coordinator;
    }

    public void RegisterForRemoteRefresh(Func<Task> onRefresh) => _core.RegisterForRemoteRefresh(onRefresh);

    public void UnregisterForRemoteRefresh(Func<Task> onRefresh) => _core.UnregisterForRemoteRefresh(onRefresh);

    public async Task NotifyFromRemoteAsync(CancellationToken cancellationToken = default)
    {
        await _coordinator.RunPullAndDrainAsync(cancellationToken);
        await _core.NotifyFromRemoteAsync(cancellationToken);
    }
}
