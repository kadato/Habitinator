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
    public void TryConsumeFocusDurationReached_TrueWhileRunningPastMilestone_PromptBlocksRepeat()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock)
        {
            FocusAlertAfter = TimeSpan.FromMinutes(25)
        };

        timer.Start();
        Assert.False(timer.TryConsumeFocusDurationReached());
        clock.Advance(TimeSpan.FromMinutes(25));
        Assert.True(timer.TryConsumeFocusDurationReached());
        Assert.True(timer.TryConsumeFocusDurationReached());

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(timer.TryConsumeFocusDurationReached());
        timer.PauseForFocusTimeUp();
        Assert.True(timer.IsRunning);
        Assert.False(timer.TryConsumeFocusDurationReached());
        clock.Advance(TimeSpan.FromMinutes(1));
        timer.ResumeAfterFocusPromptNotDone();
        Assert.False(timer.TryConsumeFocusDurationReached());
        clock.Advance(TimeSpan.FromMinutes(25));
        Assert.False(timer.TryConsumeFocusDurationReached());
    }

    [Fact]
    public void PauseForFocusTimeUp_KeepsRunning_ModalTimeCountsTowardElapsed()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock)
        {
            FocusAlertAfter = TimeSpan.FromMinutes(25)
        };

        timer.Start();
        clock.Advance(TimeSpan.FromMinutes(25));
        timer.PauseForFocusTimeUp();

        Assert.True(timer.IsRunning);
        Assert.True(timer.AwaitingFocusTimeUpPrompt);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.Elapsed);

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(27), timer.Elapsed);
    }

    [Fact]
    public void ResumeAfterFocusPromptNotDone_SuppressesFurtherAlertsForSession()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock)
        {
            FocusAlertAfter = TimeSpan.FromMinutes(25)
        };

        timer.Start();
        clock.Advance(TimeSpan.FromMinutes(25));
        Assert.True(timer.TryConsumeFocusDurationReached());
        timer.PauseForFocusTimeUp();
        clock.Advance(TimeSpan.FromMinutes(5));
        timer.ResumeAfterFocusPromptNotDone();

        Assert.False(timer.AwaitingFocusTimeUpPrompt);
        Assert.True(timer.IsRunning);
        clock.Advance(TimeSpan.FromHours(2));
        Assert.False(timer.TryConsumeFocusDurationReached());
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
        var timer = new GlobalTimerService(clock)
        {
            FocusAlertAfter = TimeSpan.FromSeconds(1)
        };
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

    [Fact]
    public void Pomodoro_StartsInWorkState()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock)
        {
            PomodoroModeEnabled = true,
            WorkDuration = TimeSpan.FromMinutes(25)
        };

        timer.Start();

        Assert.Equal(PomodoroState.Work, timer.CurrentPomodoroState);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.FocusAlertAfter);
        Assert.True(timer.IsRunning);
    }

    [Fact]
    public void Pomodoro_TransitionsToShortBreakAndLongBreakCorrectly()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock)
        {
            PomodoroModeEnabled = true,
            WorkDuration = TimeSpan.FromMinutes(25),
            ShortBreakDuration = TimeSpan.FromMinutes(5),
            LongBreakDuration = TimeSpan.FromMinutes(15),
            IntervalsBeforeLongBreak = 4
        };

        // Cycle 1: Work -> Break
        timer.Start();
        timer.IncrementCompletedIntervals();
        timer.TransitionToBreak();

        Assert.Equal(1, timer.CompletedWorkIntervalsCount);
        Assert.Equal(PomodoroState.ShortBreak, timer.CurrentPomodoroState);
        Assert.Equal(TimeSpan.FromMinutes(5), timer.FocusAlertAfter);
        Assert.False(timer.IsRunning); // "Wait for user" to start break

        // Cycle 2: Transition back to Work
        timer.TransitionToWork();
        Assert.Equal(PomodoroState.Work, timer.CurrentPomodoroState);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.FocusAlertAfter);

        // Advance count to 4 (so modulo cycle hits IntervalsBeforeLongBreak)
        timer.IncrementCompletedIntervals(); // 2
        timer.IncrementCompletedIntervals(); // 3
        timer.IncrementCompletedIntervals(); // 4
        timer.TransitionToBreak();

        Assert.Equal(4, timer.CompletedWorkIntervalsCount);
        Assert.Equal(PomodoroState.LongBreak, timer.CurrentPomodoroState);
        Assert.Equal(TimeSpan.FromMinutes(15), timer.FocusAlertAfter);
    }

    [Fact]
    public void Pomodoro_ClearTargetAndFocus_PreservesTarget()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock)
        {
            PomodoroModeEnabled = true,
            FocusAlertAfter = TimeSpan.FromMinutes(25)
        };
        timer.SelectTarget("Habit", "Read Book", Guid.NewGuid());

        timer.ClearTargetAndFocus();

        Assert.Null(timer.FocusAlertAfter);
        Assert.Equal("Habit", timer.TargetType);
        Assert.Equal("Read Book", timer.TargetId);
        Assert.NotNull(timer.BoardItemId);
    }

    [Fact]
    public void Pomodoro_ResetPomodoroSession_ClearsEverything()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock)
        {
            PomodoroModeEnabled = true
        };
        timer.Start();
        timer.IncrementCompletedIntervals();
        timer.TransitionToBreak();

        timer.ResetPomodoroSession();

        Assert.Equal(PomodoroState.Idle, timer.CurrentPomodoroState);
        Assert.Equal(0, timer.CompletedWorkIntervalsCount);
        Assert.Null(timer.FocusAlertAfter);
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public void DisplayTimeAndStatusLabel_ReflectsStateCorrectly()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock)
        {
            PomodoroModeEnabled = true,
            WorkDuration = TimeSpan.FromMinutes(25)
        };

        // Idle state
        Assert.Equal("25:00", timer.GetDisplayTime());
        Assert.Equal("Get Ready", timer.GetStatusLabel());

        // Start work state
        timer.Start();
        Assert.Equal("Focusing", timer.GetStatusLabel());
        Assert.Equal("25:00", timer.GetDisplayTime());
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("24:50", timer.GetDisplayTime());

        // Transition to Short Break
        timer.IncrementCompletedIntervals();
        timer.TransitionToBreak();
        Assert.Equal("Short Break", timer.GetStatusLabel());
        Assert.Equal("05:00", timer.GetDisplayTime());

        // Non-Pomodoro mode (Stopwatch)
        timer.PomodoroModeEnabled = false;
        timer.Reset();
        timer.Start();
        clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(23)));
        Assert.Equal("Focusing", timer.GetStatusLabel());
        Assert.Equal("05:23", timer.GetDisplayTime());
    }

    [Fact]
    public void FormatTimeSpan_HandlesHoursCorrectly()
    {
        Assert.Equal("01:05:23", GlobalTimerService.FormatTimeSpan(TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(5)).Add(TimeSpan.FromSeconds(23))));
        Assert.Equal("05:23", GlobalTimerService.FormatTimeSpan(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(23))));
    }

    private sealed class FakeClock(DateTimeOffset initial) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = initial;

        public void Advance(TimeSpan delta)
        {
            UtcNow = UtcNow.Add(delta);
        }
    }
}
