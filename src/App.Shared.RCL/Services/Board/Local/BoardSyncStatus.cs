namespace App.Shared.RCL.Services.Board.Local;

public sealed class BoardSyncStatus : IBoardSyncStatus
{
    private readonly Lock _stateLock = new();
    private bool _isOffline;
    private bool _isPaused;
    private bool _isSyncing;
    private DateTimeOffset? _lastSyncedUtc;
    private string? _syncProblemMessage;
    private int _pendingCount;

    public bool IsOffline
    {
        get
        {
            lock (_stateLock)
            {
                return _isOffline;
            }
        }
        private set => SetBool(ref _isOffline, value);
    }

    public bool IsPaused
    {
        get
        {
            lock (_stateLock)
            {
                return _isPaused;
            }
        }
        private set => SetBool(ref _isPaused, value);
    }

    public bool IsSyncing
    {
        get
        {
            lock (_stateLock)
            {
                return _isSyncing;
            }
        }
        private set => SetBool(ref _isSyncing, value);
    }

    public DateTimeOffset? LastSyncedUtc
    {
        get
        {
            lock (_stateLock)
            {
                return _lastSyncedUtc;
            }
        }
        private set
        {
            var changed = false;
            lock (_stateLock)
            {
                if (_lastSyncedUtc != value)
                {
                    _lastSyncedUtc = value;
                    changed = true;
                }
            }

            if (changed)
            {
                OnChanged();
            }
        }
    }

    public string? SyncProblemMessage
    {
        get
        {
            lock (_stateLock)
            {
                return _syncProblemMessage;
            }
        }
        private set
        {
            var changed = false;
            lock (_stateLock)
            {
                if (!string.Equals(_syncProblemMessage, value, StringComparison.Ordinal))
                {
                    _syncProblemMessage = value;
                    changed = true;
                }
            }

            if (changed)
            {
                OnChanged();
            }
        }
    }

    public int PendingCount
    {
        get
        {
            lock (_stateLock)
            {
                return _pendingCount;
            }
        }
        private set
        {
            var changed = false;
            lock (_stateLock)
            {
                if (_pendingCount != value)
                {
                    _pendingCount = value;
                    changed = true;
                }
            }

            if (changed)
            {
                OnChanged();
            }
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

    internal void SetPaused(bool value) => IsPaused = value;

    public void SetLastSynced(DateTimeOffset? value) => LastSyncedUtc = value;

    public void SetProblem(string? value) => SyncProblemMessage = value;

    public void SetPendingCount(int value) => PendingCount = Math.Max(0, value);

    public void ClearSyncState()
    {
        lock (_stateLock)
        {
            _lastSyncedUtc = null;
            _syncProblemMessage = null;
            _pendingCount = 0;
        }

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

    private void SetBool(ref bool field, bool value)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (field != value)
            {
                field = value;
                changed = true;
            }
        }

        if (changed)
        {
            OnChanged();
        }
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
