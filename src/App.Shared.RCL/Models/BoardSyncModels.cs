namespace App.Shared.RCL.Models;

public sealed record BoardSyncItem(BoardSection Section, BoardItem Item);

/// <summary>Incremental board changes since <see cref="NextCursor"/> (exclusive watermark from prior pull).</summary>
public sealed record BoardSyncDelta(
    IReadOnlyList<BoardSyncItem> Items,
    IReadOnlyList<Guid> DeletedItemIds,
    string NextCursor);
