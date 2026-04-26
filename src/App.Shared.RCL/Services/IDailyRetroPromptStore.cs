namespace App.Shared.RCL.Services;

/// <summary>Remembers the UTC calendar day when the user last dismissed the yesterday dailies prompt, so it shows at most once per day per device.</summary>
public interface IDailyRetroPromptStore
{
    /// <summary>Returns the UTC <see cref="DateOnly"/> for which a prompt was last dismissed, or <c>null</c> if never set.</summary>
    Task<DateOnly?> GetLastPromptResolvedUtcDateAsync(CancellationToken cancellationToken = default);

    /// <summary>Records that the user has seen or dismissed the prompt for the current UTC day.</summary>
    Task SetPromptResolvedForTodayAsync(CancellationToken cancellationToken = default);
}
