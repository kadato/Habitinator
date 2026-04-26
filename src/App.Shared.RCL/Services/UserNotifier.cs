using App.Shared.RCL.Models;
using MudBlazor;

namespace App.Shared.RCL.Services;

public sealed class UserNotifier : IUserNotifier, IDisposable
{
    private readonly ISnackbar _snackbar;
    private readonly INotificationSettingsService _settingsService;
    private readonly IRemoteBoardRefreshService _remoteBoardRefresh;
    private readonly IClock _clock;
    private NotificationSettings? _cache;
    private readonly Func<Task> _onRemoteRefresh;

    public UserNotifier(
        ISnackbar snackbar,
        INotificationSettingsService settingsService,
        IRemoteBoardRefreshService remoteBoardRefresh,
        IClock clock)
    {
        _snackbar = snackbar;
        _settingsService = settingsService;
        _remoteBoardRefresh = remoteBoardRefresh;
        _clock = clock;
        _settingsService.Changed += OnSettingsChanged;
        _onRemoteRefresh = OnRemoteBoardRefreshInvalidateCacheAsync;
        _remoteBoardRefresh.RegisterForRemoteRefresh(_onRemoteRefresh);
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

    private Task OnRemoteBoardRefreshInvalidateCacheAsync()
    {
        _cache = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _settingsService.Changed -= OnSettingsChanged;
        _remoteBoardRefresh.UnregisterForRemoteRefresh(_onRemoteRefresh);
    }
}
