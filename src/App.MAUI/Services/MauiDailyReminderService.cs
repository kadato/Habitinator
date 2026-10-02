using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;

using Microsoft.Extensions.Logging;

using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace App.MAUI.Services;

/// <summary>
///     Schedules a one-shot local notification for the next daily reminder time, with content derived from
///     the current board. Reschedules when settings change or the app is foregrounded so the message stays
///     up to date.
/// </summary>
public sealed partial class MauiDailyReminderService : IDisposable
{
    private const int NotificationId = 42_001;

    public const string AndroidChannelId = "habitinator.daily";
    private readonly LocalFirstBoardDataService _board;
    private readonly ILogger<MauiDailyReminderService> _logger;

    private readonly INotificationSettingsService _notificationSettings;
    private readonly IUserDateFormatService _dateFormatService;
    private readonly IUserTimeZoneService _timeZoneService;
    private readonly IUserPreferencesService _preferencesService;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    public MauiDailyReminderService(
        INotificationSettingsService notificationSettings,
        IUserDateFormatService dateFormatService,
        LocalFirstBoardDataService board,
        IUserTimeZoneService timeZoneService,
        IUserPreferencesService preferencesService,
        IClock clock,
        ILogger<MauiDailyReminderService> logger)
    {
        _notificationSettings = notificationSettings;
        _dateFormatService = dateFormatService;
        _board = board;
        _timeZoneService = timeZoneService;
        _preferencesService = preferencesService;
        _clock = clock;
        _logger = logger;
        _notificationSettings.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SynchronizeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reschedule daily reminder after settings change.");
            }
        });
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        if (!LocalNotificationCenter.Current.IsSupported)
        {
            return;
        }

        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var center = LocalNotificationCenter.Current;
            center.Cancel(NotificationId);

            var settings = await _notificationSettings.GetAsync(cancellationToken);
            if (!settings.DailyReminderEnabled || !settings.DailyReminderTime.HasValue)
            {
                return;
            }

            await _dateFormatService.InitializeAsync(cancellationToken).ConfigureAwait(false);

            var utcNow = _clock.UtcNow;
            var timeOfDay = DailyReminderSchedule.NormalizeTime(settings.DailyReminderTime);
            var next = DailyReminderSchedule.NextLocalTime(timeOfDay, utcNow, _timeZoneService, settings);

            TimeSpan? dayStart = null;
            try
            {
                var prefs = await _preferencesService.GetAsync(cancellationToken).ConfigureAwait(false);
                dayStart = prefs.DayStartLocalTime;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not load day-start for reminder content; using midnight.");
            }

            var snapshot = await _board.GetSnapshotAsync(cancellationToken);
            // Same calendar the board uses, so the notification lists what the app shows.
            var localToday = DailyReminderSchedule.ResolveToday(utcNow, _timeZoneService, dayStart);
            var (title, body) = DailyReminderText.Build(snapshot, localToday, _dateFormatService.DateFormat);

            var perm = new NotificationPermission { AskPermission = true };
            if (!await center.AreNotificationsEnabled(perm).ConfigureAwait(false)
                && (!await center.RequestNotificationPermission(perm).ConfigureAwait(false)
                    || !await center.AreNotificationsEnabled(perm).ConfigureAwait(false)))
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
                    RepeatType = NotificationRepeat.Daily
                }
            };

            await center.Show(request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to update daily reminder notification.");
        }
        finally
        {
            _syncGate.Release();
        }
    }

    /// <summary>Next <paramref name="timeOfDay" /> on the device clock, today if still ahead, else tomorrow.</summary>
    internal static DateTime NextLocalNotificationTime(TimeSpan timeOfDay)
    {
        return DailyReminderSchedule.NextLocalTime(timeOfDay, DateTimeOffset.UtcNow, tz: null, settings: null);
    }

    public void Dispose()
    {
        _notificationSettings.Changed -= OnSettingsChanged;
        _syncGate.Dispose();
    }
}
