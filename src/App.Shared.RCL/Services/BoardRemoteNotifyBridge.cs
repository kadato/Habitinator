using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

/// <summary>Public JS-invokable target for board refresh (SignalR + visibility). DotNetObjectReference requires a public type.</summary>
public sealed class BoardRemoteNotifyBridge(IRemoteBoardRefreshService refresh)
{
    [JSInvokable]
    public Task OnBoardChanged() => refresh.NotifyFromRemoteAsync();

    [JSInvokable]
    public Task OnBecameVisible() => refresh.NotifyFromRemoteAsync();
}
