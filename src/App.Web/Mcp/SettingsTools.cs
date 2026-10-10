using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Web.Data;

using Microsoft.EntityFrameworkCore;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class SettingsTools(IDbContextFactory<ApplicationDbContext> dbFactory, IHttpContextAccessor http)
{
    [McpServerTool(Name = "get_preferences", ReadOnly = true, OpenWorld = false)]
    [Description("Reads user preferences: date format, day start, timezone override, display name, theme, pomodoro setup, keyboard shortcuts.")]
    public async Task<UserPreferences> GetPreferences(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return row?.UserPreferences ?? UserPreferences.CreateDefault();
    }

    [McpServerTool(Name = "put_preferences", OpenWorld = true)]
    [Description("Updates user preferences field by field. Omit a field to keep its value. For a full replace use set_preferences_json.")]
    public async Task<UserPreferences> PutPreferences(
        [Description("Date format or null to keep.")] string? dateFormat = null,
        [Description("Day start as HH:mm or null to keep.")] string? dayStartLocalTime = null,
        [Description("Timezone override id or null to keep. Empty string clears it.")] string? timeZoneOverrideId = null,
        [Description("Display name or null to keep. Empty string clears it.")] string? displayName = null,
        [Description("Theme: System, Light, or Dark, or null to keep.")] AppTheme? theme = null,
        [Description("Pomodoro work minutes 1 to 180, or null to keep.")] int? pomodoroWorkMinutes = null,
        [Description("Pomodoro short break 1 to 60, or null to keep.")] int? pomodoroShortBreakMinutes = null,
        [Description("Pomodoro long break 1 to 120, or null to keep.")] int? pomodoroLongBreakMinutes = null,
        [Description("Pomodoro cycles before long break 1 to 12, or null to keep.")] int? pomodoroCyclesBeforeLongBreak = null,
        [Description("Keyboard shortcuts on or off, or null to keep.")] bool? enableKeyboardShortcuts = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new McpProtocolException(
                "Sign in required.", McpErrorCode.InvalidRequest);

        var prefs = row.UserPreferences ?? UserPreferences.CreateDefault();
        if (dateFormat is not null)
        {
            prefs.DateFormat = dateFormat;
        }

        if (dayStartLocalTime is not null && TimeSpan.TryParse(dayStartLocalTime, CultureInfo.InvariantCulture, out var dayStart))
        {
            prefs.DayStartLocalTime = dayStart;
        }

        if (timeZoneOverrideId is not null)
        {
            prefs.TimeZoneOverrideId = timeZoneOverrideId.Length == 0 ? null : timeZoneOverrideId;
        }

        if (displayName is not null)
        {
            prefs.DisplayName = displayName.Length == 0 ? null : displayName;
        }

        if (theme.HasValue)
        {
            prefs.Theme = theme.Value;
        }

        if (pomodoroWorkMinutes.HasValue)
        {
            prefs.PomodoroWorkDurationMinutes = pomodoroWorkMinutes.Value;
        }

        if (pomodoroShortBreakMinutes.HasValue)
        {
            prefs.PomodoroShortBreakMinutes = pomodoroShortBreakMinutes.Value;
        }

        if (pomodoroLongBreakMinutes.HasValue)
        {
            prefs.PomodoroLongBreakMinutes = pomodoroLongBreakMinutes.Value;
        }

        if (pomodoroCyclesBeforeLongBreak.HasValue)
        {
            prefs.PomodoroCyclesBeforeLongBreak = pomodoroCyclesBeforeLongBreak.Value;
        }

        if (enableKeyboardShortcuts.HasValue)
        {
            prefs.EnableKeyboardShortcuts = enableKeyboardShortcuts.Value;
        }

        row.UserPreferences = prefs.Normalize();
        await db.SaveChangesAsync(cancellationToken);
        return row.UserPreferences;
    }

    [McpServerTool(Name = "set_preferences_json", OpenWorld = true)]
    [Description("Replaces all user preferences with a JSON document. Read get_preferences first and keep every field you do not mean to change.")]
    public async Task<UserPreferences> SetPreferencesJson(
        [Description("Full preferences JSON document.")] string json,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        UserPreferences? prefs;
        try
        {
            prefs = JsonSerializer.Deserialize<UserPreferences>(json, App.Shared.RCL.Services.JsonDefaults.Api);
        }
        catch (JsonException)
        {
            throw new McpProtocolException("The json is not a valid preferences document.", McpErrorCode.InvalidParams);
        }

        if (prefs is null)
        {
            throw new McpProtocolException("The json is not a valid preferences document.", McpErrorCode.InvalidParams);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new McpProtocolException(
                "Sign in required.", McpErrorCode.InvalidRequest);
        row.UserPreferences = prefs.Normalize();
        await db.SaveChangesAsync(cancellationToken);
        return row.UserPreferences;
    }

    [McpServerTool(Name = "get_notification_settings", ReadOnly = true, OpenWorld = false)]
    [Description("Reads notification settings: toasts, daily reminder, focus timer and sync alerts, sound, quiet hours.")]
    public async Task<NotificationSettings> GetNotificationSettings(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return row?.NotificationSettings ?? NotificationSettings.CreateDefault();
    }

    [McpServerTool(Name = "put_notification_settings", OpenWorld = true)]
    [Description("Updates notification settings field by field. Omit a field to keep its value. For a full replace use set_notification_settings_json.")]
    public async Task<NotificationSettings> PutNotificationSettings(
        [Description("In-app messages on or off, or null to keep.")] bool? inAppMessagesEnabled = null,
        [Description("Success toasts on or off, or null to keep.")] bool? showSuccessToasts = null,
        [Description("Warning toasts on or off, or null to keep.")] bool? showWarningToasts = null,
        [Description("Error toasts on or off, or null to keep.")] bool? showErrorToasts = null,
        [Description("Toast duration: Short, Normal, or Long, or null to keep.")] NotificationToastDuration? toastDuration = null,
        [Description("Daily reminder on or off, or null to keep.")] bool? dailyReminderEnabled = null,
        [Description("Daily reminder as HH:mm or null to keep.")] string? dailyReminderTime = null,
        [Description("Focus timer alerts on or off, or null to keep.")] bool? focusTimerAlertsEnabled = null,
        [Description("Sync failure alerts on or off, or null to keep.")] bool? syncFailureAlertsEnabled = null,
        [Description("Sound for device notifications on or off, or null to keep.")] bool? soundEnabled = null,
        [Description("Quiet hours on or off, or null to keep.")] bool? quietHoursEnabled = null,
        [Description("Quiet window start as HH:mm UTC, or null to keep.")] string? quietHoursStartUtc = null,
        [Description("Quiet window end as HH:mm UTC, or null to keep.")] string? quietHoursEndUtc = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new McpProtocolException(
                "Sign in required.", McpErrorCode.InvalidRequest);

        var settings = row.NotificationSettings ?? NotificationSettings.CreateDefault();
        if (inAppMessagesEnabled.HasValue)
        {
            settings.InAppMessagesEnabled = inAppMessagesEnabled.Value;
        }

        if (showSuccessToasts.HasValue)
        {
            settings.ShowSuccessToasts = showSuccessToasts.Value;
        }

        if (showWarningToasts.HasValue)
        {
            settings.ShowWarningToasts = showWarningToasts.Value;
        }

        if (showErrorToasts.HasValue)
        {
            settings.ShowErrorToasts = showErrorToasts.Value;
        }

        if (toastDuration.HasValue)
        {
            settings.ToastDuration = toastDuration.Value;
        }

        if (dailyReminderEnabled.HasValue)
        {
            settings.DailyReminderEnabled = dailyReminderEnabled.Value;
        }

        if (dailyReminderTime is not null && TimeSpan.TryParse(dailyReminderTime, CultureInfo.InvariantCulture, out var reminder))
        {
            settings.DailyReminderTime = reminder;
        }

        if (focusTimerAlertsEnabled.HasValue)
        {
            settings.FocusTimerAlertsEnabled = focusTimerAlertsEnabled.Value;
        }

        if (syncFailureAlertsEnabled.HasValue)
        {
            settings.SyncFailureAlertsEnabled = syncFailureAlertsEnabled.Value;
        }

        if (soundEnabled.HasValue)
        {
            settings.SoundEnabledForDeviceNotifications = soundEnabled.Value;
        }

        if (quietHoursEnabled.HasValue)
        {
            settings.QuietHoursEnabled = quietHoursEnabled.Value;
        }

        if (quietHoursStartUtc is not null && TimeSpan.TryParse(quietHoursStartUtc, CultureInfo.InvariantCulture, out var quietStart))
        {
            settings.QuietHoursStartUtc = quietStart;
        }

        if (quietHoursEndUtc is not null && TimeSpan.TryParse(quietHoursEndUtc, CultureInfo.InvariantCulture, out var quietEnd))
        {
            settings.QuietHoursEndUtc = quietEnd;
        }

        row.NotificationSettings = settings;
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    [McpServerTool(Name = "set_notification_settings_json", OpenWorld = true)]
    [Description("Replaces all notification settings with a JSON document. Read get_notification_settings first and keep every field you do not mean to change.")]
    public async Task<NotificationSettings> SetNotificationSettingsJson(
        [Description("Full notification settings JSON document.")] string json,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        NotificationSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<NotificationSettings>(json, App.Shared.RCL.Services.JsonDefaults.Api);
        }
        catch (JsonException)
        {
            throw new McpProtocolException("The json is not a valid notification settings document.", McpErrorCode.InvalidParams);
        }

        if (settings is null)
        {
            throw new McpProtocolException("The json is not a valid notification settings document.", McpErrorCode.InvalidParams);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new McpProtocolException(
                "Sign in required.", McpErrorCode.InvalidRequest);
        row.NotificationSettings = settings;
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }
}
