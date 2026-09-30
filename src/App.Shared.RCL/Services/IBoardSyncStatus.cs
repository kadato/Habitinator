namespace App.Shared.RCL.Services;

/// <summary>Local-first sync surface for UI. Both MAUI and Web report live sync state.</summary>
public interface IBoardSyncStatus
{
    bool IsOffline { get; }

    bool IsSyncing { get; }

    DateTimeOffset? LastSyncedUtc { get; }

    /// <summary>Non-null when the last sync try failed or operations are stuck after retries.</summary>
    string? SyncProblemMessage { get; }

    event EventHandler? Changed;
}

/// <summary>Server prerender has no local store. Interactive clients use the shared local-first status.</summary>
public sealed class NoOpBoardSyncStatus : IBoardSyncStatus
{
    public bool IsOffline => false;
    public bool IsSyncing => false;
    public DateTimeOffset? LastSyncedUtc => null;
    public string? SyncProblemMessage => null;

    public event EventHandler? Changed
    {
        add { /* No-op */ }
        remove { /* No-op */ }
    }
}
