namespace App.Shared.RCL.Services;

/// <summary>
///     Remembers the local calendar day when the user last dismissed the yesterday dailies prompt, so it shows at most
///     once per day per device. Uses the user's local timezone for date boundaries.
/// </summary>
public interface IDailyRetroPromptStore
{
    /// <summary>
    ///     Returns the local <see cref="DateOnly" /> for which a prompt was last dismissed, or <c>null</c> if never set.
    ///     Uses the user's local timezone.
    /// </summary>
    Task<DateOnly?> GetLastPromptResolvedLocalDateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     [Obsolete] Use <see cref="GetLastPromptResolvedLocalDateAsync" /> for timezone-aware date tracking.
    /// </summary>
    [Obsolete("Use GetLastPromptResolvedLocalDateAsync for timezone-aware date tracking")]
    Task<DateOnly?> GetLastPromptResolvedUtcDateAsync(CancellationToken cancellationToken = default);

    /// <summary>Records that the user has seen or dismissed the prompt for the current local day.</summary>
    Task SetPromptResolvedForTodayAsync(CancellationToken cancellationToken = default);
}
