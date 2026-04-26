using App.Shared.RCL.Models;

using MudBlazor;

namespace App.Shared.RCL.Services;

public static class NotificationSettingsRules
{
    /// <summary>Whether a MudSnackbar with the given severity should be shown.</summary>
    public static bool ShouldShowToast(NotificationSettings settings, Severity severity, DateTime utcNow)
    {
        if (!settings.InAppMessagesEnabled) return false;

        if (IsInQuietHours(settings, utcNow) && severity != Severity.Error) return false;

        return severity switch
        {
            Severity.Success or Severity.Normal or Severity.Info => settings.ShowSuccessToasts,
            Severity.Warning => settings.ShowWarningToasts,
            Severity.Error => settings.ShowErrorToasts,
            _ => true
        };
    }

    public static int VisibleStateDurationMs(NotificationToastDuration preset)
    {
        return preset switch
        {
            NotificationToastDuration.Short => 2500,
            NotificationToastDuration.Normal => 5000,
            NotificationToastDuration.Long => 10_000,
            _ => 5000
        };
    }

    /// <summary>
    ///     In-app alert when a focus "time's up" is reached. Does not use quiet hours so a scheduled
    ///     focus block can still signal completion at night. Uses <see cref="NotificationSettings.FocusTimerAlertsEnabled" />;
    ///     does not require <see cref="NotificationSettings.ShowSuccessToasts" /> so the timer can surface even when
    ///     general success toasts are muted.
    /// </summary>
    public static bool ShouldShowFocusTimerEndNotification(NotificationSettings settings)
    {
        return settings.FocusTimerAlertsEnabled
               && settings.InAppMessagesEnabled;
    }

    public static bool IsInQuietHours(NotificationSettings settings, DateTime utcNow)
    {
        if (!settings.QuietHoursEnabled
            || !settings.QuietHoursStartUtc.HasValue
            || !settings.QuietHoursEndUtc.HasValue)
            return false;

        var t = utcNow.TimeOfDay;
        var a = settings.QuietHoursStartUtc.Value;
        var b = settings.QuietHoursEndUtc.Value;
        if (a == b) return false;

        if (a < b) return t >= a && t < b;

        return t >= a || t < b;
    }
}
