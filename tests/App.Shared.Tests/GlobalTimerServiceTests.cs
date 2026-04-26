using App.Shared.RCL.Services;

namespace App.Shared.Tests;

public sealed class GlobalTimerServiceTests
{
    [Fact]
    public void PauseResumeProducesExpectedElapsed()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock);

        timer.Start();
        clock.Advance(TimeSpan.FromMinutes(5));
        timer.Pause();
        clock.Advance(TimeSpan.FromMinutes(2));
        timer.Start();
        clock.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal(TimeSpan.FromMinutes(8), timer.Elapsed);
    }

    [Fact]
    public void SetManualTarget_TrimsAndUsesSessionType()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock);

        timer.SetManualTarget("  Deep work  ");

        Assert.Equal("Session", timer.TargetType);
        Assert.Equal("Deep work", timer.TargetId);
    }

    [Fact]
    public void SetManualTarget_EmptyClearsTarget()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock);

        timer.SelectTarget("Habit", "Run", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        timer.SetManualTarget("   ");

        Assert.Null(timer.TargetType);
        Assert.Null(timer.TargetId);
        Assert.Null(timer.BoardItemId);
    }

    [Fact]
    public void SelectTarget_WithBoardItemId_PersistsUntilCleared()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock);
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");

        timer.SelectTarget("Daily", "Workout", id);

        Assert.Equal(id, timer.BoardItemId);
    }

    [Fact]
    public void StopResetsElapsed()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock);

        timer.Start();
        clock.Advance(TimeSpan.FromSeconds(20));
        TimeSpan captured = timer.Stop();

        Assert.Equal(TimeSpan.FromSeconds(20), captured);
        Assert.Equal(TimeSpan.Zero, timer.Elapsed);
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public void TryConsumeFocusDurationReached_TrueWhileRunningPastMilestone_PauseClearsReadyState()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock);
        timer.FocusAlertAfter = TimeSpan.FromMinutes(25);

        timer.Start();
        Assert.False(timer.TryConsumeFocusDurationReached());
        clock.Advance(TimeSpan.FromMinutes(25));
        Assert.True(timer.TryConsumeFocusDurationReached());
        Assert.True(timer.TryConsumeFocusDurationReached());

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(timer.TryConsumeFocusDurationReached());
        timer.PauseForFocusTimeUp();
        Assert.False(timer.TryConsumeFocusDurationReached());
        clock.Advance(TimeSpan.FromMinutes(1));
        timer.ResumeAfterFocusPromptNotDone();
        Assert.False(timer.TryConsumeFocusDurationReached());
        clock.Advance(TimeSpan.FromMinutes(25));
        Assert.True(timer.TryConsumeFocusDurationReached());
    }

    [Fact]
    public void TryConsumeFocusDurationReached_NoTargetOrNotRunning()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock);

        timer.Start();
        clock.Advance(TimeSpan.FromHours(1));
        Assert.False(timer.TryConsumeFocusDurationReached());

        timer.FocusAlertAfter = TimeSpan.FromHours(1);
        Assert.False(timer.TryConsumeFocusDurationReached());
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(timer.TryConsumeFocusDurationReached());
        timer.Stop();

        Assert.False(timer.TryConsumeFocusDurationReached());
    }

    [Fact]
    public void Stop_ResetsFocusEndConsumed_SoNewSessionCanAlert()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock);
        timer.FocusAlertAfter = TimeSpan.FromSeconds(1);
        timer.Start();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(timer.TryConsumeFocusDurationReached());
        timer.Stop();
        Assert.False(timer.IsRunning);
        timer.Start();
        Assert.False(timer.TryConsumeFocusDurationReached());
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(timer.TryConsumeFocusDurationReached());
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset initial)
        {
            UtcNow = initial;
        }

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan delta)
        {
            UtcNow = UtcNow.Add(delta);
        }
    }
}
