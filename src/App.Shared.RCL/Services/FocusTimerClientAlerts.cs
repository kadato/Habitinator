using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

public sealed class FocusTimerClientAlerts(
    IJSRuntime js,
    INotificationSettingsService settingsService,
    IClock clock,
    INotificationSettingsRules notificationRules) : IFocusTimerClientAlerts
{
    private const string AlertScriptPath = "_content/App.Shared.RCL/js/focusTimerAlert.js";

    private readonly IClock _clock = clock;
    private readonly IJSRuntime _js = js;
    private readonly INotificationSettingsRules _notificationRules = notificationRules;
    private readonly INotificationSettingsService _settingsService = settingsService;

    public ValueTask NotifyTimeUpAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        return NotifyAsync("habitinatorFocusTimeUp", title, body, cancellationToken);
    }

    public ValueTask NotifyBreakTimeUpAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        return NotifyAsync("habitinatorBreakTimeUp", title, body, cancellationToken);
    }

    private async ValueTask NotifyAsync(string jsMethod, string title, string body, CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.FocusTimerAlertsEnabled)
        {
            return;
        }

        var quiet = _notificationRules.IsInQuietHours(settings, _clock.UtcNow.UtcDateTime);
        // Chime obeys quiet hours. In-app and browser OS notifications for focus do not, same as snackbar.
        var playSound = settings.SoundEnabledForDeviceNotifications && !quiet;

        await JsInvokeSafe.InvokeVoidAsync(_js, "habitinatorLoadScript", AlertScriptPath);
        await JsInvokeSafe.InvokeVoidAsync(_js, jsMethod, title, body, playSound, true);
    }
}
