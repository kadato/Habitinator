using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FsCheck;
using FsCheck.Xunit;

namespace App.Shared.Tests;

public sealed class NotificationSettingsJsonFuzzTests
{
    [Property]
    public void DeserializeOrDefault_NeverThrows(string? json)
    {
        // Deserializing arbitrary JSON/strings must never crash
        NotificationSettingsJson.DeserializeOrDefault(json);
    }

    [Property]
    public void Roundtrip_PreservesAllProperties(
        bool inAppMessagesEnabled,
        bool showSuccessToasts,
        bool showWarningToasts,
        bool showErrorToasts,
        int toastDurationVal,
        bool dailyReminderEnabled,
        TimeSpan? dailyReminderTime,
        bool focusTimerAlertsEnabled,
        bool syncFailureAlertsEnabled,
        bool soundEnabledForDeviceNotifications,
        bool quietHoursEnabled,
        TimeSpan? quietHoursStartUtc,
        TimeSpan? quietHoursEndUtc)
    {
        // Construct fuzzed settings using generated values
        var original = new NotificationSettings
        {
            InAppMessagesEnabled = inAppMessagesEnabled,
            ShowSuccessToasts = showSuccessToasts,
            ShowWarningToasts = showWarningToasts,
            ShowErrorToasts = showErrorToasts,
            ToastDuration = (NotificationToastDuration)(Math.Abs(toastDurationVal) % 3), // Normal, Short, Long
            DailyReminderEnabled = dailyReminderEnabled,
            DailyReminderTime = dailyReminderTime,
            FocusTimerAlertsEnabled = focusTimerAlertsEnabled,
            SyncFailureAlertsEnabled = syncFailureAlertsEnabled,
            SoundEnabledForDeviceNotifications = soundEnabledForDeviceNotifications,
            QuietHoursEnabled = quietHoursEnabled,
            QuietHoursStartUtc = quietHoursStartUtc,
            QuietHoursEndUtc = quietHoursEndUtc
        };

        var json = NotificationSettingsJson.Serialize(original);
        var back = NotificationSettingsJson.DeserializeOrDefault(json);

        Assert.NotNull(back);
        Assert.Equal(original.InAppMessagesEnabled, back.InAppMessagesEnabled);
        Assert.Equal(original.ShowSuccessToasts, back.ShowSuccessToasts);
        Assert.Equal(original.ShowWarningToasts, back.ShowWarningToasts);
        Assert.Equal(original.ShowErrorToasts, back.ShowErrorToasts);
        Assert.Equal(original.ToastDuration, back.ToastDuration);
        Assert.Equal(original.DailyReminderEnabled, back.DailyReminderEnabled);
        Assert.Equal(original.DailyReminderTime, back.DailyReminderTime);
        Assert.Equal(original.FocusTimerAlertsEnabled, back.FocusTimerAlertsEnabled);
        Assert.Equal(original.SyncFailureAlertsEnabled, back.SyncFailureAlertsEnabled);
        Assert.Equal(original.SoundEnabledForDeviceNotifications, back.SoundEnabledForDeviceNotifications);
        Assert.Equal(original.QuietHoursEnabled, back.QuietHoursEnabled);
        Assert.Equal(original.QuietHoursStartUtc, back.QuietHoursStartUtc);
        Assert.Equal(original.QuietHoursEndUtc, back.QuietHoursEndUtc);
    }
}
