using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.Extensions.Logging;

using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace App.MAUI.Services;

/// <summary>
///     Schedules a one-shot local notification for the next daily reminder time, with content derived from
///     the current board. Reschedules when settings change or the app is foregrounded so the message stays
///     up to date.
/// </summary>
public sealed class MauiDailyReminderService : IDisposable
{
    public const int NotificationId = 42_001;

    public const string AndroidChannelId = "habitinator.daily";
    private readonly IBoardDataService _board;
    private readonly ILogger<MauiDailyReminderService> _logger;

    private readonly INotificationSettingsService _notificationSettings;

    public MauiDailyReminderService(
        INotificationSettingsService notificationSettings,
        IBoardDataService board,
        ILogger<MauiDailyReminderService> logger)
    {
        _notificationSettings = notificationSettings;
        _board = board;
        _logger = logger;
        _notificationSettings.Changed += OnSettingsChanged;
    }

    public void Dispose()
    {
        _notificationSettings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged()
    {
        MainThread.BeginInvokeOnMainThread(() => { _ = SynchronizeAsync(); });
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        if (!LocalNotificationCenter.Current.IsSupported) return;

        try
        {
            var center = LocalNotificationCenter.Current;
            center.Cancel(NotificationId);

            var settings = await _notificationSettings.GetAsync(cancellationToken);
            if (!settings.DailyReminderEnabled || !settings.DailyReminderTime.HasValue) return;

            var timeOfDay = settings.DailyReminderTime.Value;
            var next = NextLocalNotificationTime(timeOfDay);

            var snapshot = await _board.GetSnapshotAsync(cancellationToken);
            // Use device's local timezone for the daily reminder
            var localToday = DateOnly.FromDateTime(DateTime.Now);
            var (title, body) = DailyReminderText.Build(snapshot, localToday);

            var perm = new NotificationPermission { AskPermission = true };
            if (!await center.AreNotificationsEnabled(perm).ConfigureAwait(false))
                if (!await center.RequestNotificationPermission(perm).ConfigureAwait(false)
                    || !await center.AreNotificationsEnabled(perm).ConfigureAwait(false))
                {
                    _logger.LogDebug("Daily reminder not scheduled: notification permission denied.");
                    return;
                }

            var request = new NotificationRequest
            {
                NotificationId = NotificationId,
                Title = title,
                Subtitle = "Daily reminder",
                Description = body,
                Silent = !settings.SoundEnabledForDeviceNotifications,
                Android =
                {
                    ChannelId = AndroidChannelId
                },
                Schedule =
                {
                    NotifyTime = next,
                    RepeatType = NotificationRepeat.No
                }
            };

            await center.Show(request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to update daily reminder notification.");
        }
    }

    /// <summary>Next <paramref name="timeOfDay" /> on the device clock (today if still ahead, else tomorrow).</summary>
    internal static DateTime NextLocalNotificationTime(TimeSpan timeOfDay)
    {
        if (timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1)) timeOfDay = TimeSpan.FromHours(7);

        var now = DateTime.Now;
        var today = now.Date;
        var candidate = today + timeOfDay;
        return candidate > now ? candidate : candidate.AddDays(1);
    }
}
