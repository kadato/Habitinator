#pragma warning disable MUD0012

using App.Shared.RCL.Components;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class NotificationSettingsSectionTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly INotificationSettingsService _settingsService = Substitute.For<INotificationSettingsService>();
    private readonly IRemoteBoardRefreshService _remoteBoardRefresh = Substitute.For<IRemoteBoardRefreshService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();
    private readonly IUserTimeZoneService _timeZoneService = Substitute.For<IUserTimeZoneService>();

    public NotificationSettingsSectionTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton<INotificationSettingsService>(_settingsService);
        _ctx.Services.AddSingleton<IRemoteBoardRefreshService>(_remoteBoardRefresh);
        _ctx.Services.AddSingleton<IUserNotifier>(_notifier);
        _ctx.Services.AddSingleton<IUserTimeZoneService>(_timeZoneService);

        // Render PopoverProvider to satisfy MudBlazor dropdowns/pickers/switches
        _ctx.Render<MudPopoverProvider>();

        // Default mock behaviors
        _timeZoneService.IsDetected.Returns(true);
        _timeZoneService.TimeZoneId.Returns("America/New_York");
        _timeZoneService.ConvertUtcTimeToLocal(Arg.Any<TimeSpan>()).Returns(x => x.Arg<TimeSpan>());
        _timeZoneService.ConvertLocalTimeToUtc(Arg.Any<TimeSpan>()).Returns(x => x.Arg<TimeSpan>());
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void Renders_LoadingSkeletons_Initially()
    {
        // Arrange
        var tcs = new TaskCompletionSource<NotificationSettings>();
        _settingsService.GetAsync(Arg.Any<CancellationToken>()).Returns(tcs.Task);

        // Act
        var cut = _ctx.Render<NotificationSettingsSection>();

        // Assert
        cut.FindComponents<MudSkeleton>().Should().NotBeEmpty();
    }

    [Fact]
    public void Renders_Settings_OnceLoaded()
    {
        // Arrange
        var settings = new NotificationSettings
        {
            InAppMessagesEnabled = true,
            ShowSuccessToasts = true,
            ShowWarningToasts = false,
            ShowErrorToasts = true,
            ToastDuration = NotificationToastDuration.Long,
            DailyReminderEnabled = true,
            DailyReminderTime = TimeSpan.FromHours(8),
            FocusTimerAlertsEnabled = true,
            SyncFailureAlertsEnabled = false,
            SoundEnabledForDeviceNotifications = true,
            QuietHoursEnabled = true,
            QuietHoursStartUtc = TimeSpan.FromHours(22),
            QuietHoursEndUtc = TimeSpan.FromHours(6)
        };
        _settingsService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(settings));

        // Act
        var cut = _ctx.Render<NotificationSettingsSection>();

        // Assert
        var switches = cut.FindComponents<MudSwitch<bool>>();
        switches.Should().HaveCount(9);
        switches[0].Instance.Value.Should().BeTrue(); // In-app
        switches[1].Instance.Value.Should().BeTrue(); // Success
        switches[2].Instance.Value.Should().BeFalse(); // Warning
        switches[3].Instance.Value.Should().BeTrue(); // Error
        switches[4].Instance.Value.Should().BeTrue(); // Daily
        switches[5].Instance.Value.Should().BeTrue(); // Focus
        switches[6].Instance.Value.Should().BeFalse(); // Sync
        switches[7].Instance.Value.Should().BeTrue(); // Sound
        switches[8].Instance.Value.Should().BeTrue(); // Quiet

        var select = cut.FindComponent<MudSelect<NotificationToastDuration>>();
        select.Instance.Value.Should().Be(NotificationToastDuration.Long);

        var timePickers = cut.FindComponents<MudTimePicker>();
        timePickers.Should().HaveCount(3);
        timePickers[0].Instance.Time.Should().Be(TimeSpan.FromHours(8)); // Daily Reminder Time
        timePickers[1].Instance.Time.Should().Be(TimeSpan.FromHours(22)); // Quiet Start
        timePickers[2].Instance.Time.Should().Be(TimeSpan.FromHours(6)); // Quiet End
    }

    [Fact]
    public void Disables_SubToastsAndDuration_WhenInAppDisabled()
    {
        // Arrange
        var settings = new NotificationSettings
        {
            InAppMessagesEnabled = false
        };
        _settingsService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(settings));

        // Act
        var cut = _ctx.Render<NotificationSettingsSection>();

        // Assert
        var switches = cut.FindComponents<MudSwitch<bool>>();
        switches[1].Instance.Disabled.Should().BeTrue(); // Success toast switch
        switches[2].Instance.Disabled.Should().BeTrue(); // Warning toast switch
        switches[3].Instance.Disabled.Should().BeTrue(); // Error toast switch

        var select = cut.FindComponent<MudSelect<NotificationToastDuration>>();
        select.Instance.Disabled.Should().BeTrue();
    }

    [Fact]
    public async Task AutoSaves_ToggleInAppMessages()
    {
        // Arrange
        var settings = new NotificationSettings
        {
            InAppMessagesEnabled = true
        };
        _settingsService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(settings));

        var cut = _ctx.Render<NotificationSettingsSection>();
        var switches = cut.FindComponents<MudSwitch<bool>>();

        // Act
        await cut.InvokeAsync(() => switches[0].Instance.ValueChanged.InvokeAsync(false));

        // Assert
        await _settingsService.Received().SaveAsync(Arg.Is<NotificationSettings>(s => !s.InAppMessagesEnabled));
    }

    [Fact]
    public async Task AutoSaves_DailyReminderTime_AndDisabling()
    {
        // Arrange
        var settings = new NotificationSettings
        {
            DailyReminderEnabled = true,
            DailyReminderTime = TimeSpan.FromHours(7)
        };
        _settingsService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(settings));

        var cut = _ctx.Render<NotificationSettingsSection>();
        var timePickers = cut.FindComponents<MudTimePicker>();

        // Act - Change Daily Reminder Time
        await cut.InvokeAsync(() => timePickers[0].Instance.TimeChanged.InvokeAsync(TimeSpan.FromHours(9)));

        // Assert
        await _settingsService.Received().SaveAsync(Arg.Is<NotificationSettings>(s => s.DailyReminderTime == TimeSpan.FromHours(9)));

        // Act - Disable Daily Reminder
        _settingsService.ClearReceivedCalls();
        var switches = cut.FindComponents<MudSwitch<bool>>();
        await cut.InvokeAsync(() => switches[4].Instance.ValueChanged.InvokeAsync(false));

        // Assert - Save should be called with null DailyReminderTime when disabled
        await _settingsService.Received().SaveAsync(Arg.Is<NotificationSettings>(s => !s.DailyReminderEnabled && s.DailyReminderTime == null));
    }

    [Fact]
    public async Task AutoSaves_QuietHours_WithTimeZoneConversion()
    {
        // Arrange
        var settings = new NotificationSettings
        {
            QuietHoursEnabled = true,
            QuietHoursStartUtc = TimeSpan.FromHours(22),
            QuietHoursEndUtc = TimeSpan.FromHours(6)
        };
        _settingsService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(settings));

        // Let's mock local-to-UTC conversion: shift start by 4 hours (+4h)
        _timeZoneService.ConvertLocalTimeToUtc(TimeSpan.FromHours(18)).Returns(TimeSpan.FromHours(22));
        _timeZoneService.ConvertLocalTimeToUtc(TimeSpan.FromHours(2)).Returns(TimeSpan.FromHours(6));

        var cut = _ctx.Render<NotificationSettingsSection>();
        var timePickers = cut.FindComponents<MudTimePicker>();

        // Act - Change Local start time to 18:00
        await cut.InvokeAsync(() => timePickers[1].Instance.TimeChanged.InvokeAsync(TimeSpan.FromHours(18)));

        // Assert - Model's UTC start time is saved converted
        await _settingsService.Received().SaveAsync(Arg.Is<NotificationSettings>(s => s.QuietHoursStartUtc == TimeSpan.FromHours(22)));
    }

    [Fact]
    public void Displays_ErrorAlert_OnLoadException()
    {
        // Arrange
        _settingsService.GetAsync(Arg.Any<CancellationToken>()).Returns(x => Task.FromException<NotificationSettings>(new InvalidOperationException("Database error")));

        // Act
        var cut = _ctx.Render<NotificationSettingsSection>();

        // Assert
        var alert = cut.FindComponent<MudAlert>();
        alert.Instance.Severity.Should().Be(Severity.Error);
        cut.Markup.Should().Contain("Database error");
    }

    [Fact]
    public async Task RemoteRefresh_Refetches_NotificationSettings()
    {
        // Arrange
        var settings = new NotificationSettings { InAppMessagesEnabled = true };
        _settingsService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(settings));

        Func<Task>? refreshCallback = null;
        _remoteBoardRefresh.When(x => x.RegisterForRemoteRefresh(Arg.Any<Func<Task>>()))
            .Do(callInfo => refreshCallback = callInfo.Arg<Func<Task>>());

        // Act - render registers callback
        var cut = _ctx.Render<NotificationSettingsSection>();
        refreshCallback.Should().NotBeNull();

        // Prepare new settings to load on refresh
        var refreshedSettings = new NotificationSettings { InAppMessagesEnabled = false };
        _settingsService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(refreshedSettings));

        // Trigger refresh
        await cut.InvokeAsync(async () => await refreshCallback());

        // Assert - check UI/model updated
        var switches = cut.FindComponents<MudSwitch<bool>>();
        switches[0].Instance.Value.Should().BeFalse();
    }
}
