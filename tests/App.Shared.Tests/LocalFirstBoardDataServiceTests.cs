using System.Net;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;
using App.Shared.RCL.Services.Remote;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace App.Shared.Tests;

public sealed class LocalFirstBoardDataServiceTests
{
    [Fact]
    public async Task CreateItemAsync_IsVisibleInSnapshotWithoutNetwork()
    {
        var harness = new Harness();
        var item = await harness.Board.CreateItemAsync(BoardSection.Habit, "Read", cancellationToken: CancellationToken.None);

        var snapshot = await harness.Board.GetSnapshotAsync(CancellationToken.None);

        snapshot.Habits.Should().ContainSingle(x => x.Id == item.Id && x.Title == "Read");
        (await harness.Store.ListOutboxAsync(Harness.UserKey)).Should().ContainSingle();
    }

    [Fact]
    public async Task ToggleTodoAsync_WorksOfflineAndQueuesOutbox()
    {
        var harness = new Harness();
        var ct = CancellationToken.None;
        var item = await harness.Board.CreateItemAsync(BoardSection.Todo, "Buy milk", cancellationToken: ct);

        var toggled = await harness.Board.ToggleItemAsync(BoardSection.Todo, item.Id, ct);

        Assert.NotNull(toggled);
        toggled.IsCompleted.Should().BeTrue();
        var snapshot = await harness.Board.GetSnapshotAsync(ct);
        snapshot.Todos.Should().ContainSingle(x => x.Id == item.Id && x.IsCompleted);
        (await harness.Store.ListOutboxAsync(Harness.UserKey)).Should().HaveCount(2);
    }

    [Fact]
    public async Task SnapshotAsync_IsScopedPerUserKey()
    {
        var harness = new Harness();
        var ct = CancellationToken.None;
        await harness.Board.CreateItemAsync(BoardSection.Habit, "Read", cancellationToken: ct);

        harness.Users.SetUserKey("OTHER@LOCAL");
        var snapshot = await harness.Board.GetSnapshotAsync(ct);

        snapshot.Habits.Should().BeEmpty();
    }

    private sealed class Harness
    {
        public const string UserKey = "WEBTEST@LOCAL";

        public Harness()
        {
            Users = new FakeUserKeys();
            Store = new InMemoryBoardLocalStore();
            var remote = new RemoteBoardDataService(new FailingHttpFactory());
            var services = new FakeServices();
            Board = new LocalFirstBoardDataService(
                Store,
                Users,
                remote,
                services,
                new FakeTimeZone(),
                new BoardSyncStatus(),
                NullLogger<LocalFirstBoardDataService>.Instance);
        }

        public FakeUserKeys Users { get; }

        public InMemoryBoardLocalStore Store { get; }

        public LocalFirstBoardDataService Board { get; }
    }

    private sealed class FakeUserKeys : ICurrentUserKeyProvider
    {
        private string? _key = Harness.UserKey;

        public void SetUserKey(string? key) => _key = key;

        public Task<string?> GetUserKeyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_key);

        public Task<bool> HasAuthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_key is not null);
    }

    private sealed class FakeServices : IServiceProvider
    {
        private readonly FakePreferences _prefs = new();

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IUserPreferencesService) ? _prefs : null;
    }

    private sealed class FakePreferences : IUserPreferencesService
    {
        public event EventHandler? Changed;

        public Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserPreferences());

        public Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTimeZone : IUserTimeZoneService
    {
        public string? TimeZoneId => null;

        public bool IsDetected => false;

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

    private sealed class FailingHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FailingHandler());
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}
