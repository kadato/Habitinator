namespace App.MAUI.Data;

/// <summary>Single-row metadata with Id equal to 1.</summary>
public sealed class LocalBoardStoreMetaRow
{
    public int Id { get; set; } = 1;

    /// <summary>Email or empty value. The SQLite mirror is scoped to it.</summary>
    public string? BoundUserKey { get; set; }

    /// <summary>ISO-8601 exclusive watermark for <c>GET /api/board/sync?cursor=</c>. Null after login until first successful incremental pull or full snapshot.</summary>
    public string? LastSyncCursorUtc { get; set; }
}
