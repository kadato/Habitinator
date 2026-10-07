namespace App.Shared.RCL.Services.Board.Local;

public sealed class BoardSyncStatus : IBoardSyncStatus
{
    private volatile bool _isOffline;
    private volatile bool _isSyncing;

    public bool IsOffline
    {
        get => _isOffline;
        private set
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
        private set
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
        private set
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
        private set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnChanged();
        }
    }

    public int PendingCount
    {
        get;
        private set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnChanged();
        }
    }

    private const int MaxConflicts = 20;
    private readonly Queue<BoardSyncConflict> _conflicts = [];

    /// <summary>Newest first. Holds the last resolutions so the UI can show them.</summary>
    public IReadOnlyList<BoardSyncConflict> RecentConflicts
    {
        get
        {
            lock (_conflicts)
            {
                return [.. _conflicts.Reverse()];
            }
        }
    }

    public void RecordConflict(BoardSyncConflict conflict)
    {
        lock (_conflicts)
        {
            _conflicts.Enqueue(conflict);
            while (_conflicts.Count > MaxConflicts)
            {
                _conflicts.Dequeue();
            }
        }

        OnChanged();
    }

    public event EventHandler? Changed;

    internal void SetSyncing(bool value) => IsSyncing = value;

    public void SetLastSynced(DateTimeOffset? value) => LastSyncedUtc = value;

    public void SetProblem(string? value) => SyncProblemMessage = value;

    public void SetPendingCount(int value) => PendingCount = Math.Max(0, value);

    public void ClearSyncState()
    {
        SetLastSynced(null);
        SetProblem(null);
        SetPendingCount(0);
        lock (_conflicts)
        {
            _conflicts.Clear();
        }

        OnChanged();
    }

    public void UpdateOffline(bool isOffline) => IsOffline = isOffline;

    public void RefreshConnectivity(Func<bool> isOfflineProbe)
    {
        try
        {
            UpdateOffline(isOfflineProbe());
        }
        catch
        {
            UpdateOffline(false);
        }
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
