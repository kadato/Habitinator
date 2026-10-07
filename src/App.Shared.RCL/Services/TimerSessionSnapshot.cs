namespace App.Shared.RCL.Services;

/// <summary>Timer state saved to the local store so a session survives a restart.</summary>
/// <param name="ElapsedTicks">Elapsed time at save. Restores paused, never running.</param>
/// <param name="TargetType">Board target type, <c>Session</c> for a free-text label, or null.</param>
/// <param name="TargetId">Board target title or free-text label.</param>
/// <param name="BoardItemId">Board row id when the target is a board item.</param>
/// <param name="FocusAlertAfterTicks">Pending time's up alert, if any.</param>
/// <param name="SavedAtUtc">When the snapshot was captured.</param>
public sealed record TimerSessionSnapshot(
    long ElapsedTicks,
    string? TargetType,
    string? TargetId,
    Guid? BoardItemId,
    long? FocusAlertAfterTicks,
    DateTimeOffset SavedAtUtc);
