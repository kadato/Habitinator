using App.Shared.RCL.Models;

using MudBlazor;

namespace App.Shared.RCL.Services;

public sealed class UserNotifier : IUserNotifier, IDisposable
{
    private readonly IClock _clock;
    private readonly Func<Task> _onRemoteRefresh;
    private readonly INotificationSettingsRules _notificationRules;
    private readonly IRemoteBoardRefreshService _remoteBoardRefresh;
    private readonly INotificationSettingsService _settingsService;
    private readonly ISnackbar _snackbar;
    private NotificationSettings? _cache;

    public UserNotifier(
        ISnackbar snackbar,
        INotificationSettingsService settingsService,
        IRemoteBoardRefreshService remoteBoardRefresh,
        IClock clock,
        INotificationSettingsRules notificationRules)
    {
        _snackbar = snackbar;
        _settingsService = settingsService;
        _remoteBoardRefresh = remoteBoardRefresh;
        _clock = clock;
        _notificationRules = notificationRules;
        _settingsService.Changed += OnSettingsChanged;
        _onRemoteRefresh = OnRemoteBoardRefreshInvalidateCacheAsync;
        _remoteBoardRefresh.RegisterForRemoteRefresh(_onRemoteRefresh);
    }

    public void Dispose()
    {
        _settingsService.Changed -= OnSettingsChanged;
        _remoteBoardRefresh.UnregisterForRemoteRefresh(_onRemoteRefresh);
    }

    public async ValueTask NotifyAsync(string message, Severity severity, CancellationToken cancellationToken = default)
    {
        var settings = await GetCachedAsync(cancellationToken).ConfigureAwait(false);
        if (!_notificationRules.ShouldShowToast(settings, severity, _clock.UtcNow.UtcDateTime)) return;

        var ms = _notificationRules.VisibleStateDurationMs(settings.ToastDuration);
        _snackbar.Add(message, severity, config => config.VisibleStateDuration = ms);
    }

    private async ValueTask<NotificationSettings> GetCachedAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null) return _cache;

        _cache = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        return _cache;
    }

    private void OnSettingsChanged()
    {
        _cache = null;
    }

    private Task OnRemoteBoardRefreshInvalidateCacheAsync()
    {
        _cache = null;
        return Task.CompletedTask;
    }
}
