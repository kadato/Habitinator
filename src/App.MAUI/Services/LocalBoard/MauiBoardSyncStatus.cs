using App.Shared.RCL.Services;

namespace App.MAUI.Services.LocalBoard;

public sealed partial class MauiBoardSyncStatus : IBoardSyncStatus
{
    private volatile bool _isOffline;
    private volatile bool _isSyncing;

    public MauiBoardSyncStatus()
    {
        RefreshConnectivity();
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        IsOffline = e.NetworkAccess != NetworkAccess.Internet;
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
        get;
        internal set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnChanged();
        }
    }

    public string? SyncProblemMessage
    {
        get;
        internal set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnChanged();
        }
    }

    public event EventHandler? Changed;

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

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
