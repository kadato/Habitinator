using App.MAUI.Services.LocalBoard;
using App.Shared.RCL.Models;

using Microsoft.Extensions.Logging;

namespace App.MAUI.Services;

public interface IApiSession
{
    bool IsReady { get; }
    bool IsLoggedIn { get; }
    string? Email { get; }

    /// <summary>
    ///     Raised when login state, email, or readiness changes. Blazor should refresh UI; injected session properties
    ///     are not parameters.
    /// </summary>
    event EventHandler? Changed;

    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SetSessionAsync(LoginResponse response, CancellationToken cancellationToken = default);
    Task ClearSessionAsync(CancellationToken cancellationToken = default);
}

public sealed class ApiSession : IApiSession
{
    private readonly MauiBoardHubService _hub;
    private readonly IMauiBoardLocalStoreLifecycle _localBoard;
    private readonly MauiBoardSyncCoordinator _sync;
    private readonly MauiBoardSyncStatus _syncStatus;
    private readonly IAuthTokenStore _store;
    private readonly ILogger<ApiSession> _logger;

    public ApiSession(
        IAuthTokenStore store,
        MauiBoardHubService hub,
        IMauiBoardLocalStoreLifecycle localBoard,
        MauiBoardSyncCoordinator sync,
        MauiBoardSyncStatus syncStatus,
        ILogger<ApiSession> logger)
    {
        _store = store;
        _hub = hub;
        _localBoard = localBoard;
        _sync = sync;
        _syncStatus = syncStatus;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public bool IsReady { get; private set; }
    public bool IsLoggedIn { get; private set; }
    public string? Email { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var t = await _store.GetAccessTokenAsync(cancellationToken);
        var e = await _store.GetEmailAsync(cancellationToken);
        if (string.IsNullOrEmpty(e) && !string.IsNullOrEmpty(t))
        {
            e = JwtAccessTokenDisplayClaims.TryGetEmail(t);
            if (!string.IsNullOrEmpty(e))
            {
                await _store.SetEmailAsync(e, cancellationToken);
            }
        }

        IsLoggedIn = !string.IsNullOrEmpty(t);
        Email = e;
        IsReady = true;
        OnChanged();

        if (IsLoggedIn)
        {
            StartBackgroundHubAndSync();
        }
    }

    public async Task SetSessionAsync(LoginResponse response, CancellationToken cancellationToken = default)
    {
        await _store.SetAccessTokenAsync(response.AccessToken, cancellationToken);
        await _store.SetEmailAsync(response.Email, cancellationToken);
        IsLoggedIn = true;
        Email = response.Email;
        IsReady = true;
        OnChanged();
        StartBackgroundHubAndSync();
    }

    private void StartBackgroundHubAndSync()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.EnsureConnectedAsync(CancellationToken.None);
                _sync.RequestSync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Background hub connect or board sync failed.");
            }
        });
    }

    public async Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        await _hub.DisconnectAsync(cancellationToken);
        await _localBoard.ClearAllLocalStateAsync(cancellationToken);
        _syncStatus.LastSyncedUtc = null;
        _syncStatus.SyncProblemMessage = null;
        await _store.SetAccessTokenAsync(null, cancellationToken);
        await _store.SetEmailAsync(null, cancellationToken);
        IsLoggedIn = false;
        Email = null;
        OnChanged();
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
