using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

/// <summary>
///     Public JS-invokable target for board refresh over SignalR and visibility changes. DotNetObjectReference requires a public
///     type.
/// </summary>
public sealed class BoardRemoteNotifyBridge(IRemoteBoardRefreshService refresh)
{
    [JSInvokable]
    public Task OnBoardChanged()
    {
        return refresh.NotifyFromRemoteAsync();
    }

    [JSInvokable]
    public Task OnBecameVisible()
    {
        return refresh.NotifyFromRemoteAsync();
    }
}
