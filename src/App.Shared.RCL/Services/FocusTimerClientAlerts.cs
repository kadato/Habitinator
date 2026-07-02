using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

public sealed class FocusTimerClientAlerts(
    IJSRuntime js,
    INotificationSettingsService settingsService,
    IClock clock,
    INotificationSettingsRules notificationRules) : IFocusTimerClientAlerts
{
    private readonly IClock _clock = clock;
    private readonly IJSRuntime _js = js;
    private readonly INotificationSettingsRules _notificationRules = notificationRules;
    private readonly INotificationSettingsService _settingsService = settingsService;

    public async ValueTask NotifyTimeUpAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.FocusTimerAlertsEnabled)
        {
            return;
        }

        var quiet = _notificationRules.IsInQuietHours(settings, _clock.UtcNow.UtcDateTime);
        // Chime obeys quiet hours; in-app and browser OS notifications for focus do not (same as snackbar).
        var playSound = settings.SoundEnabledForDeviceNotifications && !quiet;
        var showSystemNotification = true;

        try
        {
            await _js.InvokeVoidAsync("habitinatorLoadScript", "_content/App.Shared.RCL/js/focusTimerAlert.js").ConfigureAwait(false);
            await _js
                .InvokeVoidAsync("habitinatorFocusTimeUp", title, body, playSound, showSystemNotification)
                .ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // Ignored when JS is disconnected during disposal/navigation
        }
        catch (JSException)
        {
            // Ignored if JS execution fails or is not supported
        }
    }

    public async ValueTask NotifyBreakTimeUpAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.FocusTimerAlertsEnabled)
        {
            return;
        }

        var quiet = _notificationRules.IsInQuietHours(settings, _clock.UtcNow.UtcDateTime);
        var playSound = settings.SoundEnabledForDeviceNotifications && !quiet;
        var showSystemNotification = true;

        try
        {
            await _js.InvokeVoidAsync("habitinatorLoadScript", "_content/App.Shared.RCL/js/focusTimerAlert.js").ConfigureAwait(false);
            await _js
                .InvokeVoidAsync("habitinatorBreakTimeUp", title, body, playSound, showSystemNotification)
                .ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // Ignored when JS is disconnected during disposal/navigation
        }
        catch (JSException)
        {
            // Ignored if JS execution fails or is not supported
        }
    }
}
