using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services.Board.Local;

public sealed class BoardOutboxEntry
{
    public Guid OperationId { get; set; }

    public string UserKey { get; set; } = "";

    public BoardOutboxOperationKind Kind { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? LastAttemptUtc { get; set; }

    public string? LastError { get; set; }
}

public sealed class BoardStoreMeta
{
    public string? BoundUserKey { get; set; }

    public string? LastSyncCursorUtc { get; set; }
}
