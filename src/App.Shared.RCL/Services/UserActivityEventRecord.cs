using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public readonly record struct UserActivityEventRecord(
    DateTimeOffset OccurredAtUtc,
    ActivityEventType EventType,
    Guid? BoardItemId,
    int? DurationSeconds,
    string? CustomLabel = null);
