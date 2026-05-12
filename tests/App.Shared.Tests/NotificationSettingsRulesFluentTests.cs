using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

using MudBlazor;

using NSubstitute;

namespace App.Shared.Tests;

public sealed class NotificationSettingsRulesFluentTests
{
    [Fact]
    public void Should_block_non_error_toasts_during_quiet_hours()
    {
        var timeZoneService = Substitute.For<IUserTimeZoneService>();
        timeZoneService.ConvertToLocal(Arg.Any<DateTimeOffset>())
            .Returns(callInfo => callInfo.ArgAt<DateTimeOffset>(0));
        timeZoneService.ConvertUtcTimeToLocal(Arg.Any<TimeSpan>())
            .Returns(callInfo => callInfo.ArgAt<TimeSpan>(0));

        var settings = NotificationSettings.CreateDefault();
        settings.QuietHoursEnabled = true;
        settings.QuietHoursStartUtc = new TimeSpan(22, 0, 0);
        settings.QuietHoursEndUtc = new TimeSpan(6, 0, 0);

        var rules = new NotificationSettingsRules(timeZoneService);
        var utcNow = new DateTime(2026, 5, 12, 23, 0, 0, DateTimeKind.Utc);

        rules.ShouldShowToast(settings, Severity.Success, utcNow)
            .Should().BeFalse();
        rules.ShouldShowToast(settings, Severity.Error, utcNow)
            .Should().BeTrue();
    }

    [Fact]
    public void Should_allow_focus_timer_toast_when_enabled()
    {
        var timeZoneService = Substitute.For<IUserTimeZoneService>();
        var rules = new NotificationSettingsRules(timeZoneService);

        var settings = NotificationSettings.CreateDefault();
        settings.FocusTimerAlertsEnabled = true;
        settings.InAppMessagesEnabled = true;

        rules.ShouldShowFocusTimerEndNotification(settings)
            .Should().BeTrue();
    }
}
