using App.Shared.RCL.Models;

using MudBlazor;

namespace App.Shared.RCL.Services;

public interface INotificationSettingsRules
{
    bool ShouldShowToast(NotificationSettings settings, Severity severity, DateTime utcNow);
    int VisibleStateDurationMs(NotificationToastDuration preset);
    bool ShouldShowFocusTimerEndNotification(NotificationSettings settings);
    bool IsInQuietHours(NotificationSettings settings, DateTime utcNow);
}

public sealed class NotificationSettingsRules : INotificationSettingsRules
{
    private readonly IUserTimeZoneService _timeZoneService;

    public NotificationSettingsRules(IUserTimeZoneService timeZoneService)
    {
        _timeZoneService = timeZoneService;
    }

    /// <summary>Whether a MudSnackbar with the given severity should be shown.</summary>
    public bool ShouldShowToast(NotificationSettings settings, Severity severity, DateTime utcNow)
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

    public int VisibleStateDurationMs(NotificationToastDuration preset)
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
    public bool ShouldShowFocusTimerEndNotification(NotificationSettings settings)
    {
        return settings.FocusTimerAlertsEnabled
               && settings.InAppMessagesEnabled;
    }

    public bool IsInQuietHours(NotificationSettings settings, DateTime utcNow)
    {
        if (!settings.QuietHoursEnabled
            || !settings.QuietHoursStartUtc.HasValue
            || !settings.QuietHoursEndUtc.HasValue)
            return false;

        // Convert current UTC time to local time for comparison with quiet hours
        var localTime = _timeZoneService.ConvertToLocal(new DateTimeOffset(utcNow, TimeSpan.Zero));
        var localTimeOfDay = localTime.TimeOfDay;

        // Quiet hours are stored in UTC, so convert them to local for comparison
        var quietStartLocal = _timeZoneService.ConvertUtcTimeToLocal(settings.QuietHoursStartUtc.Value);
        var quietEndLocal = _timeZoneService.ConvertUtcTimeToLocal(settings.QuietHoursEndUtc.Value);

        if (quietStartLocal == quietEndLocal) return false;

        // Check if current local time falls within quiet hours window
        if (quietStartLocal < quietEndLocal)
        {
            // Window doesn't cross midnight (e.g., 10 PM to 6 AM would be start > end)
            return localTimeOfDay >= quietStartLocal && localTimeOfDay < quietEndLocal;
        }

        // Window crosses midnight (e.g., 10 PM to 6 AM)
        return localTimeOfDay >= quietStartLocal || localTimeOfDay < quietEndLocal;
    }
}
