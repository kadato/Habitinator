using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using MudBlazor;

namespace App.Shared.Tests;

/// <summary>Tests the toast visibility rules consumed by <see cref="UserNotifier"/>.</summary>
public sealed class NotificationSettingsRulesTests
{
    private static readonly DateTime UtcNoon = new(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Master_switch_off_blocks_all_severities()
    {
        var s = NotificationSettings.CreateDefault();
        s.InAppMessagesEnabled = false;

        Assert.False(NotificationSettingsRules.ShouldShowToast(s, Severity.Success, UtcNoon));
        Assert.False(NotificationSettingsRules.ShouldShowToast(s, Severity.Warning, UtcNoon));
        Assert.False(NotificationSettingsRules.ShouldShowToast(s, Severity.Error, UtcNoon));
    }

    [Theory]
    [InlineData(Severity.Success)]
    [InlineData(Severity.Normal)]
    [InlineData(Severity.Info)]
    public void Success_group_respects_show_success_flag(Severity severity)
    {
        var s = NotificationSettings.CreateDefault();
        s.ShowSuccessToasts = false;

        Assert.False(NotificationSettingsRules.ShouldShowToast(s, severity, UtcNoon));
    }

    [Fact]
    public void Show_warning_flag_controls_warnings()
    {
        var s = NotificationSettings.CreateDefault();
        s.ShowWarningToasts = false;

        Assert.False(NotificationSettingsRules.ShouldShowToast(s, Severity.Warning, UtcNoon));
    }

    [Fact]
    public void Show_error_flag_controls_errors()
    {
        var s = NotificationSettings.CreateDefault();
        s.ShowErrorToasts = false;

        Assert.False(NotificationSettingsRules.ShouldShowToast(s, Severity.Error, UtcNoon));
    }

    [Fact]
    public void Quiet_hours_suppress_success_but_allow_errors()
    {
        var s = NotificationSettings.CreateDefault();
        s.QuietHoursEnabled = true;
        s.QuietHoursStartUtc = TimeSpan.FromHours(10);
        s.QuietHoursEndUtc = TimeSpan.FromHours(14);
        DateTime utc = new(2026, 4, 26, 11, 0, 0, DateTimeKind.Utc);

        Assert.True(NotificationSettingsRules.IsInQuietHours(s, utc));
        Assert.False(NotificationSettingsRules.ShouldShowToast(s, Severity.Success, utc));
        Assert.True(NotificationSettingsRules.ShouldShowToast(s, Severity.Error, utc));
    }

    [Fact]
    public void Quiet_hours_overnight_window()
    {
        var s = NotificationSettings.CreateDefault();
        s.QuietHoursEnabled = true;
        s.QuietHoursStartUtc = TimeSpan.FromHours(22);
        s.QuietHoursEndUtc = TimeSpan.FromHours(7);

        Assert.True(NotificationSettingsRules.IsInQuietHours(s, new DateTime(2026, 4, 26, 23, 0, 0, DateTimeKind.Utc)));
        Assert.True(NotificationSettingsRules.IsInQuietHours(s, new DateTime(2026, 4, 26, 3, 0, 0, DateTimeKind.Utc)));
        Assert.False(NotificationSettingsRules.IsInQuietHours(s, new DateTime(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(NotificationToastDuration.Short, 2500)]
    [InlineData(NotificationToastDuration.Normal, 5000)]
    [InlineData(NotificationToastDuration.Long, 10_000)]
    public void Duration_presets_map_to_milliseconds(NotificationToastDuration preset, int expectedMs)
    {
        Assert.Equal(expectedMs, NotificationSettingsRules.VisibleStateDurationMs(preset));
    }

    [Fact]
    public void Focus_timer_end_requires_in_app_alerts_and_focus_toggle_not_success_group()
    {
        var s = NotificationSettings.CreateDefault();
        s.FocusTimerAlertsEnabled = true;
        s.InAppMessagesEnabled = true;
        s.ShowSuccessToasts = true;
        Assert.True(NotificationSettingsRules.ShouldShowFocusTimerEndNotification(s));

        s.InAppMessagesEnabled = false;
        Assert.False(NotificationSettingsRules.ShouldShowFocusTimerEndNotification(s));
        s.InAppMessagesEnabled = true;

        s.FocusTimerAlertsEnabled = false;
        Assert.False(NotificationSettingsRules.ShouldShowFocusTimerEndNotification(s));
        s.FocusTimerAlertsEnabled = true;

        s.ShowSuccessToasts = false;
        Assert.True(NotificationSettingsRules.ShouldShowFocusTimerEndNotification(s));
    }

    [Fact]
    public void Focus_timer_end_ignores_quiet_hours()
    {
        var s = NotificationSettings.CreateDefault();
        s.QuietHoursEnabled = true;
        s.QuietHoursStartUtc = TimeSpan.FromHours(0);
        s.QuietHoursEndUtc = TimeSpan.FromHours(24);
        s.ShowSuccessToasts = true;
        s.FocusTimerAlertsEnabled = true;
        s.InAppMessagesEnabled = true;
        var utc = new DateTime(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(NotificationSettingsRules.IsInQuietHours(s, utc));
        Assert.True(NotificationSettingsRules.ShouldShowFocusTimerEndNotification(s));
    }
}
