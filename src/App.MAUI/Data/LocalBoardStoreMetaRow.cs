namespace App.MAUI.Data;

/// <summary>Single-row metadata (Id = 1).</summary>
public sealed class LocalBoardStoreMetaRow
{
    public int Id { get; set; } = 1;

    /// <summary>Email (or empty) the SQLite mirror is scoped to.</summary>
    public string? BoundUserKey { get; set; }

    /// <summary>ISO-8601 exclusive watermark for <c>GET /api/board/sync?cursor=</c>; null after login until first successful incremental pull or full snapshot.</summary>
    public string? LastSyncCursorUtc { get; set; }
}
