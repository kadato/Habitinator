using App.Shared.RCL.Components;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

namespace App.Shared.RCL.Tests;

public sealed class GlobalTimerChipTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly FakeClock _clock = new();
    private readonly GlobalTimerService _timer;

    public GlobalTimerChipTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _timer = new GlobalTimerService(_clock);
        _ctx.Services.AddSingleton(_timer);
    }

    public async ValueTask DisposeAsync()
    {
        _timer.Dispose();
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void The_board_does_not_show_a_duplicate_chip()
    {
        _timer.Start();

        var cut = _ctx.Render<GlobalTimerChip>();

        cut.FindAll(".global-timer-chip").Should().BeEmpty();
    }

    [Fact]
    public void A_running_timer_shows_time_and_target_on_other_pages()
    {
        NavigateTo("/statistics");
        _timer.SelectTarget("Habit", "Write report");
        _timer.Start();
        _clock.Advance(TimeSpan.FromSeconds(75));

        var cut = _ctx.Render<GlobalTimerChip>();

        cut.Find(".global-timer-chip__time").TextContent.Should().Be("01:15");
        cut.Find(".global-timer-chip__target").TextContent.Should().Be("Write report");
        cut.Find(".global-timer-chip").GetAttribute("aria-label")
            .Should().Contain("Focus timer 01:15")
            .And.Contain("Write report");
    }

    [Fact]
    public void A_paused_timer_stays_visible_so_it_can_be_found_again()
    {
        NavigateTo("/statistics");
        _timer.Start();
        _clock.Advance(TimeSpan.FromSeconds(5));
        _timer.Pause();

        var cut = _ctx.Render<GlobalTimerChip>();

        cut.FindAll(".global-timer-chip").Should().ContainSingle();
    }

    [Fact]
    public void The_chip_hides_after_the_timer_stops()
    {
        NavigateTo("/statistics");
        _timer.Start();
        _clock.Advance(TimeSpan.FromSeconds(5));
        _timer.Stop();

        var cut = _ctx.Render<GlobalTimerChip>();

        cut.FindAll(".global-timer-chip").Should().BeEmpty();
    }

    [Fact]
    public void Clicking_the_chip_opens_the_board()
    {
        NavigateTo("/statistics");
        _timer.Start();
        var cut = _ctx.Render<GlobalTimerChip>();

        cut.Find(".global-timer-chip").Click();

        _ctx.Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/");
    }

    private void NavigateTo(string path)
    {
        var nav = _ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(path);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }
}
