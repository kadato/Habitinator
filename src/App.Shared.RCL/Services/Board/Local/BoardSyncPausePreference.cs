namespace App.Shared.RCL.Services.Board.Local;

/// <summary>
/// Persists the user's automatic-sync choice and mirrors the choice onto <see cref="BoardSyncStatus"/>.
/// When paused the coordinator makes no automatic network calls and the hub stays
/// disconnected. If you resume automatic sync or choose Sync now, the coordinator syncs once.
/// </summary>
public sealed class BoardSyncPausePreference
{
    public const string StoreKey = "habitinator.sync.paused.v1";

    private readonly BoardSyncStatus _status;
    private readonly ILocalSettingsStore _store;

    public BoardSyncPausePreference(BoardSyncStatus status, ILocalSettingsStore store)
    {
        _status = status;
        _store = store;
        _status.SetPaused(ReadPersisted());
    }

    public event EventHandler<bool>? PausedChanged;

    public bool IsPaused => _status.IsPaused;

    /// <summary>Reads the persisted choice without needing the status singleton. A null store reads as unpaused.</summary>
    public static bool IsPausedIn(ILocalSettingsStore? store)
    {
        if (store is null)
        {
            return false;
        }

        try
        {
            return string.Equals(store.Read(StoreKey, "0"), "1", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public void SetPaused(bool paused)
    {
        if (_status.IsPaused == paused)
        {
            return;
        }

        try
        {
            _store.Write(StoreKey, paused ? "1" : "0");
        }
        catch
        {
            // Best effort. The in-memory flag still takes effect for this session.
        }

        _status.SetPaused(paused);
        if (!paused)
        {
            _status.SetProblem(null);
        }

        PausedChanged?.Invoke(this, paused);
    }

    private bool ReadPersisted()
    {
        try
        {
            return string.Equals(_store.Read(StoreKey, "0"), "1", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
