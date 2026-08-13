namespace App.Shared.RCL.Models;

/// <summary>User profile preferences that affect display and scheduling.</summary>
public sealed class UserPreferences
{
    public const string DefaultDateFormat = "yyyy/MM/dd";

    public string DateFormat { get; set; } = DefaultDateFormat;

    /// <summary>Local time-of-day when the user considers a new day to start.</summary>
    public TimeSpan DayStartLocalTime { get; set; } = TimeSpan.Zero;

    /// <summary>Optional timezone override using an IANA or Windows ID.</summary>
    public string? TimeZoneOverrideId { get; set; }

    public string? DisplayName { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;

    public int PomodoroWorkDurationMinutes { get; set; } = 25;

    public int PomodoroShortBreakMinutes { get; set; } = 5;

    public int PomodoroLongBreakMinutes { get; set; } = 15;

    public int PomodoroCyclesBeforeLongBreak { get; set; } = 4;

    public bool EnableKeyboardShortcuts { get; set; } = true;

    public static UserPreferences CreateDefault()
    {
        return new UserPreferences();
    }

    /// <summary>Clamps pomodoro and day-start values to valid ranges so bad persisted values cannot silently break scheduling or timers.</summary>
    public UserPreferences Normalize()
    {
        PomodoroWorkDurationMinutes = Math.Clamp(PomodoroWorkDurationMinutes, 1, 180);
        PomodoroShortBreakMinutes = Math.Clamp(PomodoroShortBreakMinutes, 1, 60);
        PomodoroLongBreakMinutes = Math.Clamp(PomodoroLongBreakMinutes, 1, 120);
        PomodoroCyclesBeforeLongBreak = Math.Clamp(PomodoroCyclesBeforeLongBreak, 1, 12);

        if (DayStartLocalTime < TimeSpan.Zero || DayStartLocalTime >= TimeSpan.FromDays(1))
        {
            DayStartLocalTime = TimeSpan.Zero;
        }

        return this;
    }
}
