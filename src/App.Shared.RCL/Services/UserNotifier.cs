using App.Shared.RCL.Models;
using MudBlazor;

namespace App.Shared.RCL.Services;

public sealed class UserNotifier : IUserNotifier, IDisposable
{
    private readonly ISnackbar _snackbar;
    private readonly INotificationSettingsService _settingsService;
    private readonly IClock _clock;
    private NotificationSettings? _cache;

    public UserNotifier(ISnackbar snackbar, INotificationSettingsService settingsService, IClock clock)
    {
        _snackbar = snackbar;
        _settingsService = settingsService;
        _clock = clock;
        _settingsService.Changed += OnSettingsChanged;
    }

    public async ValueTask NotifyAsync(string message, Severity severity, CancellationToken cancellationToken = default)
    {
        NotificationSettings settings = await GetCachedAsync(cancellationToken).ConfigureAwait(false);
        if (!NotificationSettingsRules.ShouldShowToast(settings, severity, _clock.UtcNow.UtcDateTime))
        {
            return;
        }

        int ms = NotificationSettingsRules.VisibleStateDurationMs(settings.ToastDuration);
        _snackbar.Add(message, severity, config => config.VisibleStateDuration = ms);
    }

    public async ValueTask NotifyFocusTimerEndAsync(string message, CancellationToken cancellationToken = default)
    {
        NotificationSettings settings = await GetCachedAsync(cancellationToken).ConfigureAwait(false);
        if (!NotificationSettingsRules.ShouldShowFocusTimerEndNotification(settings))
        {
            return;
        }

        int ms = NotificationSettingsRules.VisibleStateDurationMs(settings.ToastDuration);
        _snackbar.Add(message, Severity.Success, config => config.VisibleStateDuration = ms);
    }

    private async ValueTask<NotificationSettings> GetCachedAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        _cache = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        return _cache;
    }

    private void OnSettingsChanged() =>
        _cache = null;

    public void Dispose() =>
        _settingsService.Changed -= OnSettingsChanged;
}
