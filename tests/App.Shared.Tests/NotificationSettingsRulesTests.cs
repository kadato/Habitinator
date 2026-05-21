using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

using MudBlazor;

namespace App.Shared.Tests;

/// <summary>Mock timezone service for tests that assumes UTC (no conversion).</summary>
public sealed class TestTimeZoneService : IUserTimeZoneService
{
    public string? TimeZoneId => "UTC";
    public int UtcOffsetMinutes => 0;
    public bool IsDetected => true;
    public void SetOverride(string? timeZoneId)
    {
    }
    public DateOnly LocalToday => DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly LocalYesterday => LocalToday.AddDays(-1);

    public Task InitializeAsync() => Task.CompletedTask;
    public DateTimeOffset ConvertToLocal(DateTimeOffset utcTime) => utcTime;
    public DateTimeOffset ConvertToUtc(DateTimeOffset localTime) => localTime;
    public TimeSpan ConvertLocalTimeToUtc(TimeSpan localTime) => localTime;
    public TimeSpan ConvertUtcTimeToLocal(TimeSpan utcTime) => utcTime;
    public string GetTimeZoneAbbreviation() => "UTC";
}

/// <summary>Tests the toast visibility rules consumed by <see cref="UserNotifier"/>.</summary>
public sealed class NotificationSettingsRulesTests
{
    private static readonly DateTime UtcNoon = new(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc);
    private readonly INotificationSettingsRules _rules = new NotificationSettingsRules(new TestTimeZoneService());

    [Fact]
    public void Master_switch_off_blocks_all_severities()
    {
        var s = NotificationSettings.CreateDefault();
        s.InAppMessagesEnabled = false;

        _rules.ShouldShowToast(s, Severity.Success, UtcNoon).Should().BeFalse();
        _rules.ShouldShowToast(s, Severity.Warning, UtcNoon).Should().BeFalse();
        _rules.ShouldShowToast(s, Severity.Error, UtcNoon).Should().BeFalse();
    }

    [Theory]
    [InlineData(Severity.Success)]
    [InlineData(Severity.Normal)]
    [InlineData(Severity.Info)]
    public void Success_group_respects_show_success_flag(Severity severity)
    {
        var s = NotificationSettings.CreateDefault();
        s.ShowSuccessToasts = false;

        _rules.ShouldShowToast(s, severity, UtcNoon).Should().BeFalse();
    }

    [Fact]
    public void Show_warning_flag_controls_warnings()
    {
        var s = NotificationSettings.CreateDefault();
        s.ShowWarningToasts = false;

        _rules.ShouldShowToast(s, Severity.Warning, UtcNoon).Should().BeFalse();
    }

    [Fact]
    public void Show_error_flag_controls_errors()
    {
        var s = NotificationSettings.CreateDefault();
        s.ShowErrorToasts = false;

        _rules.ShouldShowToast(s, Severity.Error, UtcNoon).Should().BeFalse();
    }

    [Fact]
    public void Quiet_hours_suppress_success_but_allow_errors()
    {
        var s = NotificationSettings.CreateDefault();
        s.QuietHoursEnabled = true;
        s.QuietHoursStartUtc = TimeSpan.FromHours(10);
        s.QuietHoursEndUtc = TimeSpan.FromHours(14);
        DateTime utc = new(2026, 4, 26, 11, 0, 0, DateTimeKind.Utc);

        _rules.IsInQuietHours(s, utc).Should().BeTrue();
        _rules.ShouldShowToast(s, Severity.Success, utc).Should().BeFalse();
        _rules.ShouldShowToast(s, Severity.Error, utc).Should().BeTrue();
    }

    [Fact]
    public void Quiet_hours_overnight_window()
    {
        var s = NotificationSettings.CreateDefault();
        s.QuietHoursEnabled = true;
        s.QuietHoursStartUtc = TimeSpan.FromHours(22);
        s.QuietHoursEndUtc = TimeSpan.FromHours(7);

        _rules.IsInQuietHours(s, new DateTime(2026, 4, 26, 23, 0, 0, DateTimeKind.Utc)).Should().BeTrue();
        _rules.IsInQuietHours(s, new DateTime(2026, 4, 26, 3, 0, 0, DateTimeKind.Utc)).Should().BeTrue();
        _rules.IsInQuietHours(s, new DateTime(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc)).Should().BeFalse();
    }

    [Theory]
    [InlineData(NotificationToastDuration.Short, 2500)]
    [InlineData(NotificationToastDuration.Normal, 5000)]
    [InlineData(NotificationToastDuration.Long, 10_000)]
    public void Duration_presets_map_to_milliseconds(NotificationToastDuration preset, int expectedMs)
    {
        _rules.VisibleStateDurationMs(preset).Should().Be(expectedMs);
    }

    [Theory]
    [InlineData(NotificationToastDuration.Short, 6000)]
    [InlineData(NotificationToastDuration.Normal, 12_000)]
    [InlineData(NotificationToastDuration.Long, 20_000)]
    public void Undo_duration_presets_are_longer_than_general_toasts(
        NotificationToastDuration preset,
        int expectedMs)
    {
        _rules.UndoVisibleStateDurationMs(preset).Should().Be(expectedMs);
        _rules.UndoVisibleStateDurationMs(preset).Should().BeGreaterThan(_rules.VisibleStateDurationMs(preset));
    }

}
