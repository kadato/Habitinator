using App.Shared.RCL.Services.Board.Local;

namespace App.Shared.RCL.Services;

/// <summary>Local-first sync surface for UI. Both MAUI and Web report live sync state.</summary>
public interface IBoardSyncStatus
{
    bool IsOffline { get; }

    bool IsSyncing { get; }

    DateTimeOffset? LastSyncedUtc { get; }

    /// <summary>Queued local writes the server has not acknowledged yet.</summary>
    int PendingCount { get; }

    /// <summary>Non-null when the last sync try failed or operations are stuck after retries.</summary>
    string? SyncProblemMessage { get; }

    /// <summary>Newest first. Holds the last auto-resolved sync conflicts.</summary>
    IReadOnlyList<BoardSyncConflict> RecentConflicts { get; }

    event EventHandler? Changed;
}

/// <summary>Server prerender has no local store. Interactive clients use the shared local-first status.</summary>
public sealed class NoOpBoardSyncStatus : IBoardSyncStatus
{
    public bool IsOffline => false;
    public bool IsSyncing => false;
    public DateTimeOffset? LastSyncedUtc => null;
    public int PendingCount => 0;
    public string? SyncProblemMessage => null;
    public IReadOnlyList<BoardSyncConflict> RecentConflicts => [];

    public event EventHandler? Changed
    {
        add { /* No-op */ }
        remove { /* No-op */ }
    }
}
