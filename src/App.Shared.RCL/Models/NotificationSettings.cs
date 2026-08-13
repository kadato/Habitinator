namespace App.Shared.RCL.Models;

/// <summary>User preferences for in-app toasts and local device notifications where supported. JSON-serializable.</summary>
public sealed class NotificationSettings
{
    public bool InAppMessagesEnabled { get; set; } = true;

    public bool ShowSuccessToasts { get; set; } = true;

    public bool ShowWarningToasts { get; set; } = true;

    public bool ShowErrorToasts { get; set; } = true;

    public NotificationToastDuration ToastDuration { get; set; } = NotificationToastDuration.Normal;

    public bool DailyReminderEnabled { get; set; } = true;

    /// <summary>Time of day for daily reminder, the date portion is ignored.</summary>
    public TimeSpan? DailyReminderTime { get; set; } = TimeSpan.FromHours(7);

    public bool FocusTimerAlertsEnabled { get; set; } = true;

    public bool SyncFailureAlertsEnabled { get; set; } = true;

    public bool SoundEnabledForDeviceNotifications { get; set; } = true;

    public bool QuietHoursEnabled { get; set; }

    /// <summary>Start of quiet window, UTC time-of-day.</summary>
    public TimeSpan? QuietHoursStartUtc { get; set; }

    /// <summary>End of quiet window, UTC time-of-day.</summary>
    public TimeSpan? QuietHoursEndUtc { get; set; }

    public static NotificationSettings CreateDefault()
    {
        return new NotificationSettings();
    }
}
