namespace App.Shared.RCL.Services;

/// <summary>Notifies a board view when the server (or another device) reports that board data may have changed.</summary>
public interface IRemoteBoardRefreshService
{
    void RegisterForRemoteRefresh(Func<Task> onRefresh);

    void UnregisterForRemoteRefresh(Func<Task> onRefresh);

    /// <summary>Invoked from SignalR/JS. Runs all registered board refresh callbacks (e.g. multiple tabs/circuits).</summary>
    Task NotifyFromRemoteAsync(CancellationToken cancellationToken = default);
}
