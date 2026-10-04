using App.Shared.RCL.Services;

using FluentAssertions;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class UpcomingViewStateStoreTests
{
    [Fact]
    public async Task RoundTrips_View_And_Filter()
    {
        var local = new MemoryStore();
        var session = Substitute.For<IClientSessionProvider>();
        session.Email.Returns((string?)null);
        var store = new UpcomingViewStateStore(local, session);

        (await store.GetAsync()).Should().BeNull();

        await store.SetAsync(new UpcomingViewState("Week", "Dailies"));

        var loaded = await store.GetAsync();
        loaded.Should().NotBeNull();
        loaded?.View.Should().Be("Week");
        loaded?.Filter.Should().Be("Dailies");
    }

    [Fact]
    public async Task Returns_Null_On_Corrupt_Json()
    {
        var local = new MemoryStore();
        var session = Substitute.For<IClientSessionProvider>();
        session.Email.Returns((string?)null);
        var store = new UpcomingViewStateStore(local, session);

        local.Write("habitinator.upcomingView.v1", "{not-json");

        (await store.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Isolates_By_Account()
    {
        var local = new MemoryStore();
        var aliceSession = Substitute.For<IClientSessionProvider>();
        aliceSession.Email.Returns("alice@example.com");
        var bobSession = Substitute.For<IClientSessionProvider>();
        bobSession.Email.Returns("bob@example.com");

        var alice = new UpcomingViewStateStore(local, aliceSession);
        var bob = new UpcomingViewStateStore(local, bobSession);

        await alice.SetAsync(new UpcomingViewState("Day", "Todos"));

        (await bob.GetAsync()).Should().BeNull();

        await bob.SetAsync(new UpcomingViewState("Month", "All"));

        (await alice.GetAsync())?.View.Should().Be("Day");
        (await bob.GetAsync())?.View.Should().Be("Month");
    }

    private sealed class MemoryStore : ILocalSettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Read(string key, string? defaultValue = null) =>
            _values.TryGetValue(key, out var value) ? value : defaultValue;

        public void Write(string key, string value) => _values[key] = value;
    }
}
