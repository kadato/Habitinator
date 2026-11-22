using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class JsDailyRetroPromptStoreTests
{
    [Fact]
    public async Task SetPromptResolvedForTodayAsync_WritesTheEffectiveUserDay()
    {
        var store = new MemorySettingsStore();
        var service = new JsDailyRetroPromptStore(store, new FixedSession(), new FixedUserDay(new DateOnly(2026, 11, 21)));

        await service.SetPromptResolvedForTodayAsync();

        store.Values.Values.Should().ContainSingle().Which.Should().Be("2026-11-21");
    }

    private sealed class FixedUserDay(DateOnly today) : IUserDayService
    {
#pragma warning disable CS0067 // Interface event is not raised by this test double.
        public event EventHandler? DayChanged;
#pragma warning restore CS0067

        public DateOnly Today => today;

        public TimeSpan DayStartLocalTime => TimeSpan.Zero;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void CheckForDayChange()
        {
        }
    }

    private sealed class FixedSession : IClientSessionProvider
    {
        public bool IsLoggedIn => true;

        public string? Email => "test@example.test";

#pragma warning disable CS0067 // Interface event is not raised by this test double.
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }

    private sealed class MemorySettingsStore : ILocalSettingsStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public string? Read(string key, string? defaultValue = null) =>
            Values.TryGetValue(key, out var value) ? value : defaultValue;

        public void Write(string key, string value) => Values[key] = value;
    }
}
