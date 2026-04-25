namespace App.Web.Data;

public sealed class UserActivityEventEntity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = default!;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public ActivityEventType EventType { get; set; }

    /// <summary>Target board item when applicable; may reference a deleted item.</summary>
    public Guid? BoardItemId { get; set; }

    /// <summary>Only for <see cref="ActivityEventType.TimerSession"/>: focus duration in whole seconds.</summary>
    public int? DurationSeconds { get; set; }
}
