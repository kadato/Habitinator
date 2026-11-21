using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class SettingsActivityEventStoreTests
{
    private const string EventsKey = "habitinator_activity_events_v1";

    [Fact]
    public async Task AppendAsync_PersistsRapidAppendsOnceAfterTheDebounce()
    {
        var store = new MemoryLocalSettingsStore();
        using var events = new SettingsActivityEventStore(store);

        for (var i = 0; i < 5; i++)
        {
            await events.AppendAsync(new UserActivityEventRecord(
                DateTimeOffset.UtcNow.AddSeconds(i), ActivityEventType.HabitPlus, null, null, $"Event {i}"));
        }

        var persisted = await WaitForPersistedEventsAsync(store);
        persisted.Should().HaveCount(5);
        persisted.Select(x => x.CustomLabel).Should().Contain("Event 4");
    }

    [Fact]
    public async Task ClearAsync_CancelsAPendingSave()
    {
        var store = new MemoryLocalSettingsStore();
        using var events = new SettingsActivityEventStore(store);
        await events.AppendAsync(new UserActivityEventRecord(
            DateTimeOffset.UtcNow, ActivityEventType.HabitPlus, null, null, "Event"));

        await events.ClearAsync();
        await Task.Delay(400);

        store.Read(EventsKey).Should().BeNullOrEmpty();
        (await events.GetAllAsync()).Should().BeEmpty();
    }

    private static async Task<List<UserActivityEventRecord>> WaitForPersistedEventsAsync(MemoryLocalSettingsStore store)
    {
        for (var i = 0; i < 50; i++)
        {
            var raw = store.Read(EventsKey);
            if (!string.IsNullOrEmpty(raw))
            {
                return JsonSerializer.Deserialize<List<UserActivityEventRecord>>(raw, JsonDefaults.Api) ?? [];
            }

            await Task.Delay(20);
        }

        return [];
    }

    private sealed class MemoryLocalSettingsStore : ILocalSettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Read(string key, string? defaultValue = null) =>
            _values.TryGetValue(key, out var value) ? value : defaultValue;

        public void Write(string key, string value) => _values[key] = value;
    }
}
