using App.Shared.RCL.Services;

using Microsoft.Maui.Networking;

namespace App.MAUI.Services.LocalBoard;

public sealed class MauiBoardSyncStatus : IBoardSyncStatus, IDisposable
{
    private volatile bool _isOffline;
    private volatile bool _isSyncing;
    private DateTimeOffset? _lastSyncedUtc;
    private string? _syncProblemMessage;

    public MauiBoardSyncStatus()
    {
        RefreshConnectivity();
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        IsOffline = e.NetworkAccess != NetworkAccess.Internet;
    }

    public void Dispose()
    {
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
    }

    public bool IsOffline
    {
        get => _isOffline;
        internal set
        {
            if (_isOffline == value)
            {
                return;
            }

            _isOffline = value;
            OnChanged();
        }
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        internal set
        {
            if (_isSyncing == value)
            {
                return;
            }

            _isSyncing = value;
            OnChanged();
        }
    }

    public DateTimeOffset? LastSyncedUtc
    {
        get => _lastSyncedUtc;
        internal set
        {
            if (_lastSyncedUtc == value)
            {
                return;
            }

            _lastSyncedUtc = value;
            OnChanged();
        }
    }

    public string? SyncProblemMessage
    {
        get => _syncProblemMessage;
        internal set
        {
            if (_syncProblemMessage == value)
            {
                return;
            }

            _syncProblemMessage = value;
            OnChanged();
        }
    }

    public event EventHandler? Changed;

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    internal void RefreshConnectivity()
    {
        try
        {
            var access = Connectivity.Current.NetworkAccess;
            IsOffline = access != NetworkAccess.Internet;
        }
        catch
        {
            IsOffline = false;
        }
    }
}
