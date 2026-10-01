using System.Net;
using System.Text;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;
using App.Shared.RCL.Services.Remote;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace App.Shared.Tests;

/// <summary>Tests the shared local-first core against a scripted HTTP board API. Covers outbox drain, delta pull, and 409 LWW.</summary>
public sealed class LocalFirstBoardSyncTests
{
    [Fact]
    public async Task DrainCreateAsync_PersistsServerFieldsAndEmptiesOutbox()
    {
        var harness = new Harness();
        var id = Guid.NewGuid();
        var serverTime = DateTimeOffset.UtcNow;
        await harness.Board.CreateItemAsync(BoardSection.Habit, "Read", id, CancellationToken.None);
        harness.Server.Responder = _ => JsonResponse(
            HttpStatusCode.OK,
            new BoardItem(id, "Read", ServerUpdatedAtUtc: serverTime, CreatedAtUtc: serverTime, SortOrder: 1));

        var drained = await harness.Board.TryDrainOneOutboxOperationAsync(CancellationToken.None);

        drained.Should().BeTrue();
        (await harness.Store.ListOutboxAsync(Harness.UserKey)).Should().BeEmpty();
        var row = await harness.Store.FindItemAsync(Harness.UserKey, id);
        Assert.NotNull(row);
        row.ServerUpdatedAtUtc.Should().Be(serverTime);
        row.AwaitingServerCreate.Should().BeFalse();
    }

    [Fact]
    public async Task PullMirrorAsync_SnapshotThenDeltaAppliesUpsertsDeletesAndCursor()
    {
        var harness = new Harness();
        var kept = new BoardItem(Guid.NewGuid(), "Kept", ServerUpdatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10), CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10), SortOrder: 1);
        var added = new BoardItem(Guid.NewGuid(), "Added", ServerUpdatedAtUtc: DateTimeOffset.UtcNow, CreatedAtUtc: DateTimeOffset.UtcNow, SortOrder: 2);
        var nextCursor = DateTimeOffset.UtcNow.ToString("O");
        harness.Server.Responder = request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/api/board/sync", StringComparison.Ordinal))
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    new BoardSyncDelta(
                        [new BoardSyncItem(BoardSection.Todo, added)],
                        [kept.Id],
                        nextCursor));
            }

            return JsonResponse(HttpStatusCode.OK, new BoardSnapshot([], [], [kept]));
        };

        var first = await harness.Board.TryPullRemoteMirrorAsync(CancellationToken.None);
        first.Should().BeTrue();
        (await harness.Board.GetSnapshotAsync(CancellationToken.None)).Todos.Should().ContainSingle(x => x.Id == kept.Id);

        var second = await harness.Board.TryPullRemoteMirrorAsync(CancellationToken.None);
        second.Should().BeTrue();
        var snapshot = await harness.Board.GetSnapshotAsync(CancellationToken.None);
        snapshot.Todos.Should().ContainSingle(x => x.Id == added.Id);
        (await harness.Store.GetMetaAsync()).LastSyncCursorUtc.Should().Be(nextCursor);
    }

    [Fact]
    public async Task DrainConflictAsync_LocalNewerKeepsDeviceVersionAndRetries()
    {
        var harness = new Harness();
        var id = Guid.NewGuid();
        await harness.Board.CreateItemAsync(BoardSection.Habit, "Read", id, CancellationToken.None);
        var staleServer = new BoardItem(
            id,
            "Server title",
            ServerUpdatedAtUtc: DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAtUtc: DateTimeOffset.UtcNow.AddHours(-1),
            SortOrder: 1);
        harness.Server.Responder = _ => ConflictResponse(staleServer);

        var conflicted = await harness.Board.TryDrainOneOutboxOperationAsync(CancellationToken.None);

        conflicted.Should().BeFalse();
        var pending = await harness.Store.ListOutboxAsync(Harness.UserKey);
        pending.Should().ContainSingle();
        pending[0].AttemptCount.Should().Be(0);

        var serverTime = DateTimeOffset.UtcNow;
        harness.Server.Responder = _ => JsonResponse(
            HttpStatusCode.OK,
            new BoardItem(id, "Read", ServerUpdatedAtUtc: serverTime, CreatedAtUtc: serverTime, SortOrder: 1));
        var retried = await harness.Board.TryDrainOneOutboxOperationAsync(CancellationToken.None);

        retried.Should().BeTrue();
        (await harness.Store.ListOutboxAsync(Harness.UserKey)).Should().BeEmpty();
    }

    [Fact]
    public async Task DrainConflictAsync_ServerNewerKeepsServerVersionAndDropsOp()
    {
        var harness = new Harness();
        var id = Guid.NewGuid();
        await harness.Board.CreateItemAsync(BoardSection.Todo, "Local", id, CancellationToken.None);
        var freshServer = new BoardItem(
            id,
            "Server wins",
            ServerUpdatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
            CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
            SortOrder: 1);
        harness.Server.Responder = _ => ConflictResponse(freshServer);

        var conflicted = await harness.Board.TryDrainOneOutboxOperationAsync(CancellationToken.None);

        conflicted.Should().BeFalse();
        (await harness.Store.ListOutboxAsync(Harness.UserKey)).Should().BeEmpty();
        var snapshot = await harness.Board.GetSnapshotAsync(CancellationToken.None);
        snapshot.Todos.Should().ContainSingle(x => x.Id == id && x.Title == "Server wins");
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T value) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, JsonDefaults.Api), Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage ConflictResponse(BoardItem serverItem) =>
        JsonResponse(HttpStatusCode.Conflict, new { item = serverItem });

    private sealed class Harness
    {
        public const string UserKey = "SYNCTEST@LOCAL";

        public Harness()
        {
            Server = new ScriptedBoardServer();
            Store = new InMemoryBoardLocalStore();
            var remote = new RemoteBoardDataService(new StubHttpFactory(Server));
            Board = new LocalFirstBoardDataService(
                Store,
                new FakeUserKeys(),
                remote,
                new FakeServices(),
                new FakeTimeZone(),
                new BoardSyncStatus(),
                NullLogger<LocalFirstBoardDataService>.Instance);
        }

        public ScriptedBoardServer Server { get; }

        public InMemoryBoardLocalStore Store { get; }

        public LocalFirstBoardDataService Board { get; }
    }

    private sealed class ScriptedBoardServer : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Responder(request));
    }

    private sealed class StubHttpFactory(ScriptedBoardServer server) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(server) { BaseAddress = new Uri("https://test.local/") };
    }

    private sealed class FakeUserKeys : ICurrentUserKeyProvider
    {
        public Task<string?> GetUserKeyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(Harness.UserKey);

        public Task<bool> HasAuthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
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
}
