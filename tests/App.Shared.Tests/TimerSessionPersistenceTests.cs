using App.Shared.RCL.Services;

namespace App.Shared.Tests;

public sealed class TimerSessionPersistenceTests
{
    [Fact]
    public void Capture_PreservesElapsedTargetAndAlert()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock);
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");

        timer.SelectTarget("Habit", "Run", id);
        timer.FocusAlertAfter = TimeSpan.FromMinutes(25);
        timer.Start();
        clock.Advance(TimeSpan.FromMinutes(6));

        var snapshot = timer.CaptureState();

        Assert.Equal(TimeSpan.FromMinutes(6).Ticks, snapshot.ElapsedTicks);
        Assert.Equal("Habit", snapshot.TargetType);
        Assert.Equal("Run", snapshot.TargetId);
        Assert.Equal(id, snapshot.BoardItemId);
        Assert.Equal(TimeSpan.FromMinutes(25).Ticks, snapshot.FocusAlertAfterTicks);
    }

    [Fact]
    public void Restore_SetsPausedSessionWithTargetAndAlert()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock);
        var id = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var snapshot = new TimerSessionSnapshot(
            TimeSpan.FromMinutes(12).Ticks,
            "Daily",
            "Workout",
            id,
            TimeSpan.FromMinutes(10).Ticks,
            clock.UtcNow);

        var restored = timer.RestoreState(snapshot);

        Assert.True(restored);
        Assert.False(timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(12), timer.Elapsed);
        Assert.Equal("Daily", timer.TargetType);
        Assert.Equal("Workout", timer.TargetId);
        Assert.Equal(id, timer.BoardItemId);
        Assert.Equal(TimeSpan.FromMinutes(10), timer.FocusAlertAfter);
    }

    [Fact]
    public void Restore_EmptySnapshot_ReturnsFalseAndChangesNothing()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock);

        Assert.False(timer.RestoreState(null));
        Assert.False(timer.RestoreState(new TimerSessionSnapshot(0, null, null, null, null, clock.UtcNow)));
        Assert.Equal(TimeSpan.Zero, timer.Elapsed);
        Assert.Null(timer.TargetId);
    }

    [Fact]
    public void Store_RoundTrip_PreservesSession()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var timer = new GlobalTimerService(clock);
        var settings = new MemoryLocalSettingsStore();
        var store = new TimerSessionStore(settings);

        timer.SetManualTarget("Deep work");
        timer.Start();
        clock.Advance(TimeSpan.FromMinutes(4));
        timer.Pause();
        store.Save(timer);

        var fresh = new GlobalTimerService(clock);

        Assert.True(store.TryRestore(fresh));
        Assert.Equal(TimeSpan.FromMinutes(4), fresh.Elapsed);
        Assert.Equal("Deep work", fresh.TargetId);
        Assert.False(fresh.IsRunning);
    }

    [Fact]
    public void Store_ClearsIdleTimerAndIgnoresCorruptPayload()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var settings = new MemoryLocalSettingsStore();
        var store = new TimerSessionStore(settings);

        store.Save(new GlobalTimerService(clock));

        var fresh = new GlobalTimerService(clock);
        Assert.False(store.TryRestore(fresh));

        settings.Write("habitinator.timer.session", "{broken");
        Assert.False(store.TryRestore(new GlobalTimerService(clock)));
    }

    private sealed class MemoryLocalSettingsStore : ILocalSettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Read(string key, string? defaultValue = null) =>
            _values.TryGetValue(key, out var value) ? value : defaultValue;

        public void Write(string key, string value) => _values[key] = value;
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset initial)
        {
            UtcNow = initial;
        }

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan span) => UtcNow += span;
    }
}
