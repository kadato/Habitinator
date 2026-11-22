using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace App.Shared.Tests;

public sealed class UserDayServiceTests
{
    [Fact]
    public async Task Today_AppliesTheConfiguredDayStart()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 22, 2, 0, 0, TimeSpan.Zero));
        var service = NewService(clock, TimeSpan.FromHours(4));

        await service.InitializeAsync();

        service.Today.Should().Be(new DateOnly(2026, 11, 21));
        service.DayStartLocalTime.Should().Be(TimeSpan.FromHours(4));
    }

    [Fact]
    public async Task CheckForDayChange_RaisesOnceAtTheNextLocalDay()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 22, 2, 0, 0, TimeSpan.Zero));
        var service = NewService(clock, TimeSpan.FromHours(4));
        await service.InitializeAsync();

        var raised = 0;
        service.DayChanged += (_, _) => raised++;

        clock.Advance(TimeSpan.FromHours(3)); // 05:00 local, now the next effective day.
        service.Today.Should().Be(new DateOnly(2026, 11, 22));
        service.CheckForDayChange();
        service.CheckForDayChange();

        raised.Should().Be(1);
        service.Today.Should().Be(new DateOnly(2026, 11, 22));
    }

    [Fact]
    public async Task ChangingDayStart_RaisesDayChangedWhenTheEffectiveDayMoves()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 22, 2, 0, 0, TimeSpan.Zero));
        var preferences = new MutablePreferences(TimeSpan.Zero);
        var service = new UserDayService(clock, new FixedTimeZone(), preferences, NullLogger<UserDayService>.Instance);
        await service.InitializeAsync();
        service.Today.Should().Be(new DateOnly(2026, 11, 22));

        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DayChanged += (_, _) => changed.TrySetResult();

        preferences.SetDayStart(TimeSpan.FromHours(4));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        service.Today.Should().Be(new DateOnly(2026, 11, 21));
    }

    private static UserDayService NewService(MutableClock clock, TimeSpan dayStart) =>
        new(clock, new FixedTimeZone(), new FixedPreferences(dayStart), NullLogger<UserDayService>.Instance);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }

    private sealed class FixedTimeZone : IUserTimeZoneService
    {
        public string? TimeZoneId => "UTC";

        public bool IsDetected => true;

        public DateOnly LocalToday => DateOnly.FromDateTime(DateTime.UtcNow);

        public void SetOverride(string? timeZoneId)
        {
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public DateTimeOffset ConvertToLocal(DateTimeOffset utcTime) => utcTime;

        public DateTimeOffset ConvertToUtc(DateTimeOffset localTime) => localTime;

        public TimeSpan ConvertLocalTimeToUtc(TimeSpan localTime) => localTime;

        public TimeSpan ConvertUtcTimeToLocal(TimeSpan utcTime) => utcTime;

        public string GetTimeZoneAbbreviation() => "UTC";
    }

    private sealed class FixedPreferences(TimeSpan dayStart) : IUserPreferencesService
    {
#pragma warning disable CS0067 // Interface event is not raised by this test double.
        public event EventHandler? Changed;
#pragma warning restore CS0067

        public Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserPreferences { DayStartLocalTime = dayStart });

        public Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class MutablePreferences(TimeSpan dayStart) : IUserPreferencesService
    {
        private TimeSpan _dayStart = dayStart;

        public event EventHandler? Changed;

        public void SetDayStart(TimeSpan value)
        {
            _dayStart = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserPreferences { DayStartLocalTime = _dayStart });

        public Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
