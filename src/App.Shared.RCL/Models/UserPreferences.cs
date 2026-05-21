namespace App.Shared.RCL.Models;

/// <summary>User profile preferences that affect display and scheduling.</summary>
public sealed class UserPreferences
{
    public string DateFormat { get; set; } = "yyyy/MM/dd";

    /// <summary>Local time-of-day when the user considers a new day to start.</summary>
    public TimeSpan DayStartLocalTime { get; set; } = TimeSpan.Zero;

    /// <summary>Optional timezone override (IANA/Windows ID).</summary>
    public string? TimeZoneOverrideId { get; set; }

    public string? DisplayName { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;

    public static UserPreferences CreateDefault()
    {
        return new UserPreferences();
    }
}
