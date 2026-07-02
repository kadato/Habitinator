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

    public int PomodoroWorkDurationMinutes { get; set; } = 25;

    public int PomodoroShortBreakMinutes { get; set; } = 5;

    public int PomodoroLongBreakMinutes { get; set; } = 15;

    public int PomodoroCyclesBeforeLongBreak { get; set; } = 4;

    public static UserPreferences CreateDefault()
    {
        return new UserPreferences();
    }
}
