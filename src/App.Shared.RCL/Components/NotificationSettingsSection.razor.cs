#pragma warning disable S3881 // Dispose is implemented in the generated Razor part
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class NotificationSettingsSection : IDisposable
{
    [Parameter]
    public bool ShowTitle { get; set; } = true;

    private NotificationSettings? _model;
    private TimeSpan? _dailyReminderTime;
    private TimeSpan? _quietStart;
    private TimeSpan? _quietEnd;
    private bool _loading = true;
    private string? _loadError;

    protected override async Task OnInitializedAsync()
    {
        RemoteBoardRefresh.RegisterForRemoteRefresh(HandleRemoteSettingsRefreshedAsync);
        try
        {
            var loaded = await NotificationSettingsService.GetAsync();
            ApplyModel(loaded);
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Marshals hub-pushed refreshes onto the Blazor dispatcher.</summary>
    private Task HandleRemoteSettingsRefreshedAsync()
    {
        return InvokeAsync(OnRemoteSettingsRefreshedAsync);
    }

    private async Task OnRemoteSettingsRefreshedAsync()
    {
        try
        {
            var loaded = await NotificationSettingsService.GetAsync();
            ApplyModel(loaded);
            _loadError = null;
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
        }

        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        RemoteBoardRefresh.UnregisterForRemoteRefresh(HandleRemoteSettingsRefreshedAsync);
        GC.SuppressFinalize(this);
    }

    private void ApplyModel(NotificationSettings s)
    {
        _model = Clone(s);
        _dailyReminderTime = s.DailyReminderTime ?? TimeSpan.FromHours(7);

        // Convert UTC times to local for display
        _quietStart = s.QuietHoursStartUtc.HasValue
            ? TimeZoneService.ConvertUtcTimeToLocal(s.QuietHoursStartUtc.Value)
            : TimeSpan.FromHours(22);
        _quietEnd = s.QuietHoursEndUtc.HasValue
            ? TimeZoneService.ConvertUtcTimeToLocal(s.QuietHoursEndUtc.Value)
            : TimeSpan.FromHours(7);
    }

    private static NotificationSettings Clone(NotificationSettings s)
    {
        return NotificationSettingsJson.DeserializeOrDefault(NotificationSettingsJson.Serialize(s));
    }

    private Task OnInAppChanged(bool v)
    {
        return SetAndSaveAsync(m => m.InAppMessagesEnabled = v);
    }

    private Task OnShowSuccessToastsChanged(bool v)
    {
        return SetAndSaveAsync(m => m.ShowSuccessToasts = v);
    }

    private Task OnShowWarningToastsChanged(bool v)
    {
        return SetAndSaveAsync(m => m.ShowWarningToasts = v);
    }

    private Task OnShowErrorToastsChanged(bool v)
    {
        return SetAndSaveAsync(m => m.ShowErrorToasts = v);
    }

    private Task OnToastDurationChanged(NotificationToastDuration v)
    {
        return SetAndSaveAsync(m => m.ToastDuration = v);
    }

    private Task OnDailyReminderEnabledChanged(bool v)
    {
        return SetAndSaveAsync(m => m.DailyReminderEnabled = v);
    }

    private Task OnDailyReminderTimeChanged(TimeSpan? t)
    {
        if (_model is null)
        {
            return Task.CompletedTask;
        }

        _dailyReminderTime = t;
        return SetAndSaveAsync(m => m.DailyReminderTime = t);
    }

    private Task OnQuietHoursEnabledChanged(bool v)
    {
        return SetAndSaveAsync(m => m.QuietHoursEnabled = v);
    }

    private Task OnFocusTimerAlertsEnabledChanged(bool v)
    {
        return SetAndSaveAsync(m => m.FocusTimerAlertsEnabled = v);
    }

    private Task OnSyncFailureAlertsEnabledChanged(bool v)
    {
        return SetAndSaveAsync(m => m.SyncFailureAlertsEnabled = v);
    }

    private Task OnSoundEnabledForDeviceNotificationsChanged(bool v)
    {
        return SetAndSaveAsync(m => m.SoundEnabledForDeviceNotifications = v);
    }

    private Task OnQuietStartChanged(TimeSpan? t)
    {
        if (_model is null)
        {
            return Task.CompletedTask;
        }

        _quietStart = t;
        // Convert local time to UTC for storage
        return SetAndSaveAsync(m => m.QuietHoursStartUtc = t.HasValue
            ? TimeZoneService.ConvertLocalTimeToUtc(t.Value)
            : null);
    }

    private Task OnQuietEndChanged(TimeSpan? t)
    {
        if (_model is null)
        {
            return Task.CompletedTask;
        }

        _quietEnd = t;
        // Convert local time to UTC for storage
        return SetAndSaveAsync(m => m.QuietHoursEndUtc = t.HasValue
            ? TimeZoneService.ConvertLocalTimeToUtc(t.Value)
            : null);
    }

    private Task SetAndSaveAsync(Action<NotificationSettings> mutate)
    {
        if (_model is null)
        {
            return Task.CompletedTask;
        }

        mutate(_model);
        return AutoSaveAsync();
    }

    private async Task AutoSaveAsync()
    {
        if (_model is null)
        {
            return;
        }

        try
        {
            SyncTimePickersToModel();
            await NotificationSettingsService.SaveAsync(_model);
        }
        catch (Exception ex)
        {
            await Notifier.NotifyAsync($"Could not save settings: {ex.Message}", Severity.Error);
        }
    }

    private void SyncTimePickersToModel()
    {
        if (_model is null)
        {
            return;
        }

        _model.DailyReminderTime = _model.DailyReminderEnabled ? _dailyReminderTime : null;
        if (_model.QuietHoursEnabled)
        {
            // Convert local times to UTC for storage
            _model.QuietHoursStartUtc = _quietStart.HasValue
                ? TimeZoneService.ConvertLocalTimeToUtc(_quietStart.Value)
                : null;
            _model.QuietHoursEndUtc = _quietEnd.HasValue
                ? TimeZoneService.ConvertLocalTimeToUtc(_quietEnd.Value)
                : null;
        }
        else
        {
            _model.QuietHoursStartUtc = null;
            _model.QuietHoursEndUtc = null;
        }
    }

    private string GetTimeZoneDisplay()
    {
        if (!TimeZoneService.IsDetected)
        {
            return "UTC";
        }

        return TimeZoneService.TimeZoneId ?? TimeZoneService.GetTimeZoneAbbreviation();
    }


}
