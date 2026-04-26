using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

public sealed class FocusTimerClientAlerts : IFocusTimerClientAlerts
{
    private readonly IClock _clock;
    private readonly IJSRuntime _js;
    private readonly INotificationSettingsService _settingsService;

    public FocusTimerClientAlerts(
        IJSRuntime js,
        INotificationSettingsService settingsService,
        IClock clock)
    {
        _js = js;
        _settingsService = settingsService;
        _clock = clock;
    }

    public async ValueTask NotifyTimeUpAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.FocusTimerAlertsEnabled) return;

        var quiet = NotificationSettingsRules.IsInQuietHours(settings, _clock.UtcNow.UtcDateTime);
        // Chime obeys quiet hours; in-app and browser OS notifications for focus do not (same as snackbar).
        var playSound = settings.SoundEnabledForDeviceNotifications && !quiet;
        var showSystemNotification = true;

        try
        {
            await _js
                .InvokeVoidAsync("habitinatorFocusTimeUp", title, body, playSound, showSystemNotification)
                .ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
    }
}
