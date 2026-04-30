using App.Shared.RCL.Models;

namespace App.MAUI.Services;

public sealed class StoredUserActivityEvent
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public ActivityEventType EventType { get; set; }

    public Guid? BoardItemId { get; set; }

    public int? DurationSeconds { get; set; }

    public string? CustomLabel { get; set; }
}
