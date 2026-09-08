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

public sealed class GlobalTimerPanelTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IUserPreferencesService _preferencesService = Substitute.For<IUserPreferencesService>();
    private readonly GlobalTimerService _timerService = new(new SystemClock());
    private readonly IRenderedComponent<MudPopoverProvider> _popoverProvider;

    public GlobalTimerPanelTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton<IUserPreferencesService>(_preferencesService);
        _ctx.Services.AddSingleton(_timerService);

        _preferencesService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new UserPreferences()));

        _popoverProvider = _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void RendersModeToggle_WithVisibleStopwatchAndPomodoroLabels()
    {
        // Act
        var cut = _ctx.Render<GlobalTimerPanel>();

        // Assert
        var stopwatchBtn = cut.Find("button[aria-label='Stopwatch mode']");
        var pomodoroBtn = cut.Find("button[aria-label='Pomodoro mode']");

        stopwatchBtn.TextContent.Should().Contain("Stopwatch");
        pomodoroBtn.TextContent.Should().Contain("Pomodoro");
    }

    [Fact]
    public void ClickingPomodoroToggle_EnablesPomodoroModeInTimerService()
    {
        // Arrange
        _timerService.PomodoroModeEnabled = false;
        var cut = _ctx.Render<GlobalTimerPanel>();

        // Act
        var pomodoroBtn = cut.Find("button[aria-label='Pomodoro mode']");
        pomodoroBtn.Click();

        // Assert
        _timerService.PomodoroModeEnabled.Should().BeTrue();

        var stopwatchBtn = cut.Find("button[aria-label='Stopwatch mode']");
        pomodoroBtn = cut.Find("button[aria-label='Pomodoro mode']");

        pomodoroBtn.GetAttribute("aria-pressed").Should().Be("true");
        stopwatchBtn.GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void ClickingStopwatchToggle_DisablesPomodoroModeInTimerService()
    {
        // Arrange
        _timerService.PomodoroModeEnabled = true;
        var cut = _ctx.Render<GlobalTimerPanel>();

        // Act
        var stopwatchBtn = cut.Find("button[aria-label='Stopwatch mode']");
        stopwatchBtn.Click();

        // Assert
        _timerService.PomodoroModeEnabled.Should().BeFalse();

        var pomodoroBtn = cut.Find("button[aria-label='Pomodoro mode']");
        stopwatchBtn = cut.Find("button[aria-label='Stopwatch mode']");

        stopwatchBtn.GetAttribute("aria-pressed").Should().Be("true");
        pomodoroBtn.GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void ClickingInfoButton_OpensAndClosesHelpPopover()
    {
        // Arrange
        _timerService.PomodoroModeEnabled = false;
        var cut = _ctx.Render<GlobalTimerPanel>();

        // Initially popover is not open
        var popover = cut.FindComponents<MudPopover>().Single(p => p.Instance.Class?.Contains("timer-focus-help-popover") == true);
        popover.Instance.Open.Should().BeFalse();

        // Act - Click the info button adornment
        var infoBtn = cut.Find("button[aria-label=\"About time's up alerts\"]");
        infoBtn.Click();

        // Assert - Popover is now open and rendered in popover provider
        popover.Instance.Open.Should().BeTrue();
        _popoverProvider.Markup.Should().Contain("Time's up alerts");
        _popoverProvider.Markup.Should().Contain(FocusDurationInput.HelpTooltip);

        // Act - Click the close button
        var closeBtn = _popoverProvider.Find("button[aria-label='Close help']");
        closeBtn.Click();

        // Assert - Popover is closed
        popover.Instance.Open.Should().BeFalse();
    }

    [Fact]
    public void ClickingInfoButton_TogglesPopoverClosed()
    {
        // Arrange
        _timerService.PomodoroModeEnabled = false;
        var cut = _ctx.Render<GlobalTimerPanel>();
        var popover = cut.FindComponents<MudPopover>().Single(p => p.Instance.Class?.Contains("timer-focus-help-popover") == true);

        // Act - Open popover
        var infoBtn = cut.Find("button[aria-label=\"About time's up alerts\"]");
        infoBtn.Click();
        popover.Instance.Open.Should().BeTrue();

        // Act - Click info button again to toggle closed
        infoBtn.Click();
        popover.Instance.Open.Should().BeFalse();
    }

    [Fact]
    public void PressingEscape_ClosesHelpPopover()
    {
        // Arrange
        _timerService.PomodoroModeEnabled = false;
        var cut = _ctx.Render<GlobalTimerPanel>();
        var popover = cut.FindComponents<MudPopover>().Single(p => p.Instance.Class?.Contains("timer-focus-help-popover") == true);

        // Open popover
        var infoBtn = cut.Find("button[aria-label=\"About time's up alerts\"]");
        infoBtn.Click();
        popover.Instance.Open.Should().BeTrue();

        // Act - Press Escape on focus field
        var focusField = cut.Find(".timer-focus-field");
        focusField.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        // Assert - Popover is closed
        popover.Instance.Open.Should().BeFalse();
    }
}
