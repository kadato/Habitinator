namespace App.Shared.RCL.Models;

public enum BoardMutationStatus
{
    Ok,
    NotFound,
    Conflict
}

/// <summary>Result of a mutating board API operation with optional optimistic concurrency.</summary>
public sealed record BoardMutationResult(BoardMutationStatus Status, BoardItem? Item);
