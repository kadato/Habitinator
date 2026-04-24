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

        timer.SelectTarget("Habit", "Run");
        timer.SetManualTarget("   ");

        Assert.Null(timer.TargetType);
        Assert.Null(timer.TargetId);
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
