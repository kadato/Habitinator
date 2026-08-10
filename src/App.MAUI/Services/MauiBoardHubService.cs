using App.Shared.RCL.Hubs;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace App.MAUI.Services;

#pragma warning disable CA1001, S2930 // DI singleton: owns long-lived disposable state and is never disposed by the container.
public sealed partial class MauiBoardHubService(
    IAuthTokenStore tokens,
    IRemoteBoardRefreshService refresh,
    MauiApiEndpointOptions api,
    ILogger<MauiBoardHubService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private HubConnection? _connection;

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        var t = await tokens.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(t))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { State: HubConnectionState.Connected })
            {
                return;
            }

            await DisposeConnectionAsync();

            var baseAddress = new Uri(api.BaseUrlWithTrailingSlash, UriKind.Absolute);
            var hubUri = new Uri(baseAddress, "hubs/board");
            var hub = new HubConnectionBuilder()
                .WithUrl(
                    hubUri,
                    o => { o.AccessTokenProvider = () => tokens.GetAccessTokenAsync(CancellationToken.None); })
                .WithAutomaticReconnect()
                .Build();

            hub.On(BoardHubClient.BoardChanged, OnBoardChangedAsync);
            _connection = hub;
            try
            {
                await _connection.StartAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "SignalR board hub could not connect to {HubUrl}. Live sync is off until the API is running and reachable.",
                    hubUri);
                await DisposeConnectionAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await DisposeConnectionAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task OnBoardChangedAsync()
    {
        try
        {
            await refresh.NotifyFromRemoteAsync(_lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SignalR board-changed notification failed.");
        }
    }

    private async Task DisposeConnectionAsync()
    {
        if (_connection is null)
        {
            return;
        }

        var connection = _connection;
        _connection = null;
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to dispose the board hub connection.");
        }
    }
}
#pragma warning restore CA1001, S2930
