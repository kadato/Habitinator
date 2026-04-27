using App.Shared.RCL.Models;

using Microsoft.EntityFrameworkCore;

namespace App.MAUI.Data;

[Index(nameof(UserKey), nameof(CreatedAtUtc))]
public sealed class BoardOutboxRow
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
