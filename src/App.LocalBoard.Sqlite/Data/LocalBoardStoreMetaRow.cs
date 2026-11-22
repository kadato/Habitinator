namespace App.LocalBoard.Sqlite.Data;

/// <summary>Single-row metadata with Id equal to 1.</summary>
public sealed class LocalBoardStoreMetaRow
{
    public int Id { get; set; } = 1;

    /// <summary>User key that owns the SQLite mirror. Null when no user is bound.</summary>
    public string? BoundUserKey { get; set; }

    /// <summary>
    ///     Opaque cursor from the last incremental pull, sent as the <c>cursor</c> query parameter
    ///     to <c>GET /api/board/sync</c>. Null after login until the first pull or full snapshot.
    /// </summary>
    public string? LastSyncCursorUtc { get; set; }

    /// <summary>ISO-8601 timestamp of the last full snapshot mirror replace. Null means never.</summary>
    public string? LastFullMirrorUtc { get; set; }
}
