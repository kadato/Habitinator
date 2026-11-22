namespace App.Shared.RCL.Services;

/// <summary>
///     Single source of truth for the user's current local day. All day checks,
///     day-relative board reads, and rollover subscriptions go through this service,
///     so the whole app changes days at the same moment.
/// </summary>
public interface IUserDayService
{
    /// <summary>Raised when the effective local day changes, including after a day-start preference change.</summary>
    event EventHandler? DayChanged;

    /// <summary>The current effective local day. Safe to read after <see cref="InitializeAsync" />.</summary>
    DateOnly Today { get; }

    /// <summary>The user's configured local day start.</summary>
    TimeSpan DayStartLocalTime { get; }

    /// <summary>Detects the timezone and loads the user's day-start preference. Safe to call repeatedly.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Compares the live clock with the last seen day and raises <see cref="DayChanged" /> when needed.</summary>
    void CheckForDayChange();
}
