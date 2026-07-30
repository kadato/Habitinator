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

    public GlobalTimerPanelTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton<IUserPreferencesService>(_preferencesService);
        _ctx.Services.AddSingleton(_timerService);

        _preferencesService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new UserPreferences()));

        _ctx.Render<MudPopoverProvider>();
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
}
