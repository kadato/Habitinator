using App.Shared.RCL.Hubs;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace App.MAUI.Services;

public sealed class MauiBoardHubService
{
    private readonly MauiApiEndpointOptions _api;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<MauiBoardHubService> _logger;
    private readonly IRemoteBoardRefreshService _refresh;
    private readonly IAuthTokenStore _tokens;
    private HubConnection? _connection;

    public MauiBoardHubService(
        IAuthTokenStore tokens,
        IRemoteBoardRefreshService refresh,
        MauiApiEndpointOptions api,
        ILogger<MauiBoardHubService> logger)
    {
        _tokens = tokens;
        _refresh = refresh;
        _api = api;
        _logger = logger;
    }

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        var t = await _tokens.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(t)) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { State: HubConnectionState.Connected }) return;

            if (_connection is not null)
            {
                try
                {
                    await _connection.DisposeAsync();
                }
                catch
                {
                    // ignore
                }

                _connection = null;
            }

            var baseAddress = new Uri(_api.BaseUrlWithTrailingSlash, UriKind.Absolute);
            var hubUri = new Uri(baseAddress, "hubs/board");
            var hub = new HubConnectionBuilder()
                .WithUrl(
                    hubUri,
                    o => { o.AccessTokenProvider = () => _tokens.GetAccessTokenAsync(CancellationToken.None); })
                .WithAutomaticReconnect()
                .Build();

            hub.On(
                BoardHubClient.BoardChanged,
                () => _refresh.NotifyFromRemoteAsync(CancellationToken.None));
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
                _logger.LogWarning(
                    ex,
                    "SignalR board hub could not connect to {HubUrl}. Live sync is off until the API is running and reachable.",
                    hubUri);
                try
                {
                    await hub.DisposeAsync();
                }
                catch
                {
                    // ignore
                }

                _connection = null;
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
            if (_connection is not null)
            {
                try
                {
                    await _connection.DisposeAsync();
                }
                catch
                {
                    // ignore
                }

                _connection = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
