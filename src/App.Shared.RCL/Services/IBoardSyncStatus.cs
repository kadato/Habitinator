namespace App.Shared.RCL.Services;

/// <summary>MAUI local-first sync surface for UI. Web uses <see cref="NoOpBoardSyncStatus" />.</summary>
public interface IBoardSyncStatus
{
    bool IsOffline { get; }

    bool IsSyncing { get; }

    DateTimeOffset? LastSyncedUtc { get; }

    /// <summary>Non-null when the last sync try failed or operations are stuck after retries.</summary>
    string? SyncProblemMessage { get; }

    event EventHandler? Changed;
}

/// <summary>Web / non-MAUI hosts: no local sync layer.</summary>
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
