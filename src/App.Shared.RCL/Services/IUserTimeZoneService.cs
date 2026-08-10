namespace App.Shared.RCL.Services;

/// <summary>Provides the user's local timezone and conversion utilities.</summary>
public interface IUserTimeZoneService
{
    /// <summary>The detected timezone ID (e.g., "America/New_York"), or null if not detected.</summary>
    string? TimeZoneId { get; }

    /// <summary>Whether the timezone has been successfully detected.</summary>
    bool IsDetected { get; }

    /// <summary>Overrides the timezone ID used for conversions (null to use detected).</summary>
    void SetOverride(string? timeZoneId);

    /// <summary>
    ///     Initializes the timezone service by detecting the user's timezone.
    ///     Should be called once at app startup.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    ///     Converts a UTC DateTimeOffset to the user's local time.
    ///     If timezone is not detected, returns the original time unchanged.
    /// </summary>
    DateTimeOffset ConvertToLocal(DateTimeOffset utcTime);

    /// <summary>
    ///     Converts a local DateTimeOffset to UTC.
    ///     If timezone is not detected, assumes the time is already UTC.
    /// </summary>
    DateTimeOffset ConvertToUtc(DateTimeOffset localTime);

    /// <summary>
    ///     Gets the current date in the user's local timezone.
    ///     This is used for daily scheduling (when do dailies reset).
    /// </summary>
    DateOnly LocalToday { get; }

    /// <summary>
    ///     Converts a local TimeSpan (time-of-day) to UTC time-of-day.
    ///     Handles date rollover (e.g., 11 PM EST = 4 AM UTC next day).
    /// </summary>
    TimeSpan ConvertLocalTimeToUtc(TimeSpan localTime);

    /// <summary>
    ///     Converts a UTC TimeSpan (time-of-day) to local time-of-day.
    ///     Handles date rollover.
    /// </summary>
    TimeSpan ConvertUtcTimeToLocal(TimeSpan utcTime);

    /// <summary>
    ///     Gets a display-friendly timezone abbreviation (e.g., "UTC+2", "EST").
    /// </summary>
    string GetTimeZoneAbbreviation();
}
