using App.Shared.RCL.Services.Board.Local;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

/// <summary>
///     Public JS-invokable target for board refresh over SignalR and visibility changes. DotNetObjectReference requires a public
///     type.
/// </summary>
public sealed class BoardRemoteNotifyBridge(IRemoteBoardRefreshService refresh, IServiceProvider services)
{
    [JSInvokable]
    public Task OnBoardChanged()
    {
        return refresh.NotifyFromRemoteAsync();
    }

    [JSInvokable]
    public async Task OnBecameVisible()
    {
        try
        {
            services.GetService<IBoardSyncRequestor>()?.RequestSync();
        }
        catch
        {
            // Best effort. Refresh below still runs.
        }

        await refresh.NotifyFromRemoteAsync();
    }

    [JSInvokable]
    public Task OnWentOffline()
    {
        try
        {
            var status = services.GetService<BoardSyncStatus>();
            if (status is not null)
            {
                status.UpdateOffline(true);
                if (string.IsNullOrEmpty(status.SyncProblemMessage))
                {
                    status.SetProblem("Offline. Board changes stay on this device until you reconnect.");
                }
            }
        }
        catch
        {
            // Best effort. Coordinator probe corrects state on next tick.
        }

        return Task.CompletedTask;
    }
}
