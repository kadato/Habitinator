namespace App.Shared.RCL.Services.Board.Local;

/// <summary>How one sync conflict was resolved.</summary>
public enum BoardSyncConflictOutcome
{
    /// <summary>Both sides matched. The server copy stayed.</summary>
    Identical,

    /// <summary>The device edit was newer and won.</summary>
    KeptDevice,

    /// <summary>The server edit was newer and won.</summary>
    KeptServer
}

/// <summary>One auto-resolved sync conflict for the conflict log.</summary>
public sealed record BoardSyncConflict(
    DateTimeOffset OccurredAtUtc,
    Guid ItemId,
    string Title,
    BoardSyncConflictOutcome Outcome,
    string ChangedFields);
