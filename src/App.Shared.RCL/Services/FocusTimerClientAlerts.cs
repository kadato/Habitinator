using App.Shared.RCL.Models;
using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

public sealed class FocusTimerClientAlerts : IFocusTimerClientAlerts
{
    private readonly IJSRuntime _js;
    private readonly INotificationSettingsService _settingsService;
    private readonly IClock _clock;

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
        NotificationSettings settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.FocusTimerAlertsEnabled)
        {
            return;
        }

        bool quiet = NotificationSettingsRules.IsInQuietHours(settings, _clock.UtcNow.UtcDateTime);
        // Chime obeys quiet hours; in-app and browser OS notifications for focus do not (same as snackbar).
        bool playSound = settings.SoundEnabledForDeviceNotifications && !quiet;
        bool showSystemNotification = true;

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
