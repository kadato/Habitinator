using System.Net;
using System.Text;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;
using App.Shared.RCL.Services.Remote;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Shared.Tests;

/// <summary>Covers the Data saver pause. Automatic sync makes no network calls while Sync now still runs.</summary>
public sealed class BoardSyncPauseTests
{
    [Fact]
    public void SetPaused_RaisesChangedOnlyOnChange()
    {
        var status = new BoardSyncStatus();
        var raised = 0;
        status.Changed += (_, _) => raised++;

        status.SetPaused(true);
        status.IsPaused.Should().BeTrue();
        status.SetPaused(true);
        raised.Should().Be(1);
    }

    [Fact]
    public void ClearSyncState_PreservesPausedPreference()
    {
        var status = new BoardSyncStatus();
        status.SetPaused(true);
        status.SetPendingCount(3);

        status.ClearSyncState();

        status.IsPaused.Should().BeTrue();
        status.PendingCount.Should().Be(0);
    }

    [Fact]
    public void PausePreference_ReadsPersistedValue()
    {
        var store = new StubSettingsStore(new Dictionary<string, string>
        {
            [BoardSyncPausePreference.StoreKey] = "1"
        });
        var status = new BoardSyncStatus();

        _ = new BoardSyncPausePreference(status, store);

        status.IsPaused.Should().BeTrue();
    }

    [Fact]
    public void PausePreference_SetPaused_PersistsAndRaisesEvent()
    {
        var store = new StubSettingsStore([]);
        var status = new BoardSyncStatus();
        var preference = new BoardSyncPausePreference(status, store);
        bool? seen = null;
        preference.PausedChanged += (_, paused) => seen = paused;

        preference.SetPaused(true);

        status.IsPaused.Should().BeTrue();
        store.Values[BoardSyncPausePreference.StoreKey].Should().Be("1");
        seen.Should().BeTrue();
    }

    [Fact]
    public async Task AutomaticRun_WhenPaused_MakesNoNetworkCallsAndKeepsOutbox()
    {
        var harness = new Harness();
        var ct = CancellationToken.None;
        var id = Guid.NewGuid();
        await harness.Board.CreateItemAsync(BoardSection.Habit, "Read", id, ct);
        (await harness.Store.ListOutboxAsync(Harness.UserKey, ct)).Should().NotBeEmpty();
        harness.Status.SetPaused(true);

        await harness.Coordinator.RunPullAndDrainAsync(true, ct);

        harness.Server.HitCount.Should().Be(0);
        (await harness.Store.ListOutboxAsync(Harness.UserKey, ct)).Should().NotBeEmpty();
        harness.Status.SyncProblemMessage.Should().Be(BoardSyncCoordinator.PausedProblemMessage);
    }

    [Fact]
    public async Task SyncNow_WhenPaused_BypassesPauseAndDrainsOutbox()
    {
        var harness = new Harness();
        var ct = CancellationToken.None;
        var id = Guid.NewGuid();
        var serverTime = DateTimeOffset.UtcNow;
        await harness.Board.CreateItemAsync(BoardSection.Habit, "Read", id, ct);
        harness.Server.Responder = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    new BoardItem(id, "Read", ServerUpdatedAtUtc: serverTime, CreatedAtUtc: serverTime, SortOrder: 1));
            }

            return JsonResponse(HttpStatusCode.OK, new BoardSnapshot([], [], []));
        };
        harness.Status.SetPaused(true);

        await harness.Coordinator.SyncNowAsync(ct);

        harness.Server.HitCount.Should().BeGreaterThan(0);
        (await harness.Store.ListOutboxAsync(Harness.UserKey, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task StreakMap_WhenPaused_MakesNoNetworkCallsAndUsesLocalCounters()
    {
        var harness = new Harness();
        var ct = CancellationToken.None;
        var id = Guid.NewGuid();
        await harness.Board.CreateItemAsync(BoardSection.Daily, "Read", id, ct);
        harness.Status.SetPaused(true);

        var map = await harness.Board.GetStreakMapAsync(ct);

        harness.Server.HitCount.Should().Be(0);
        map.Should().ContainKey(id);
    }

    [Fact]
    public async Task SettingsSaveAndRead_WhenPaused_MakeNoNetworkCalls()
    {
        var handler = new CountingHandler();
        var store = new StubSettingsStore(new Dictionary<string, string>
        {
            [BoardSyncPausePreference.StoreKey] = "1"
        });
        var settings = new TestSettingsService(
            new StubSettingsHttpFactory(handler),
            new FakeSession(),
            store,
            NullLogger<TestSettingsService>.Instance);

        await settings.SaveAsync(new UserPreferences(), CancellationToken.None);
        _ = await settings.GetAsync(CancellationToken.None);

        handler.HitCount.Should().Be(0);
        settings.HasPending().Should().BeTrue();
    }

    [Fact]
    public async Task ActivityLog_WhenPaused_QueuesLocallyWithoutNetwork()
    {
        var handler = new CountingHandler();
        var store = new StubSettingsStore(new Dictionary<string, string>
        {
            [BoardSyncPausePreference.StoreKey] = "1"
        });
        using var service = new RemoteUserActivityLogService(
            new StubSettingsHttpFactory(handler),
            statsReader: null,
            eventStore: null,
            localStore: store,
            clock: null);

        await service.LogTimerSessionAsync(TimeSpan.FromMinutes(5), null, "focus", CancellationToken.None);

        handler.HitCount.Should().Be(0);
        (await service.GetPendingCountAsync(CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task StatsOverview_WhenPaused_BuildsLocallyWithoutNetwork()
    {
        var handler = new StatsCountingHandler();
        var harness = new Harness();
        var ct = CancellationToken.None;
        var id = Guid.NewGuid();
        await harness.Board.CreateItemAsync(BoardSection.Daily, "Read", id, ct);
        await harness.Board.GetSnapshotAsync(ct);
        var services = new ServiceCollection();
        services.AddSingleton<IBoardDataService>(harness.Board);
        var provider = services.BuildServiceProvider();
        using var offline = new OfflineActivityStatisticsProvider(
            provider,
            new FakeEventStore(),
            NullLogger<OfflineActivityStatisticsProvider>.Instance);
        var expected = await offline.BuildOverviewAsync(null, null, ct);
        handler.Responder = _ => JsonResponse(HttpStatusCode.OK, expected);
        var remote = new RemoteActivityStatisticsReader(new StubStatsHttpFactory(handler), new StubSettingsStore([]));
        var pauseStore = new StubSettingsStore(new Dictionary<string, string>
        {
            [BoardSyncPausePreference.StoreKey] = "1"
        });
        var reader = new PauseAwareActivityStatisticsReader(remote, offline, pauseStore);

        var actual = await reader.GetOverviewAsync(null, null, ct);

        handler.HitCount.Should().Be(0);
        JsonSerializer.Serialize(actual, JsonDefaults.Api).Should().Be(JsonSerializer.Serialize(expected, JsonDefaults.Api));
    }

    [Fact]
    public async Task StatsOverview_WhenUnpaused_ReadsFromServer()
    {
        var handler = new StatsCountingHandler();
        var harness = new Harness();
        var ct = CancellationToken.None;
        var id = Guid.NewGuid();
        await harness.Board.CreateItemAsync(BoardSection.Daily, "Read", id, ct);
        await harness.Board.GetSnapshotAsync(ct);
        var services = new ServiceCollection();
        services.AddSingleton<IBoardDataService>(harness.Board);
        var provider = services.BuildServiceProvider();
        using var offline = new OfflineActivityStatisticsProvider(
            provider,
            new FakeEventStore(),
            NullLogger<OfflineActivityStatisticsProvider>.Instance);
        var expected = await offline.BuildOverviewAsync(null, null, ct);
        handler.Responder = _ => JsonResponse(HttpStatusCode.OK, expected);
        var remote = new RemoteActivityStatisticsReader(new StubStatsHttpFactory(handler), new StubSettingsStore([]));
        var reader = new PauseAwareActivityStatisticsReader(remote, offline, new StubSettingsStore([]));

        var actual = await reader.GetOverviewAsync(null, null, ct);

        handler.HitCount.Should().Be(1);
        JsonSerializer.Serialize(actual, JsonDefaults.Api).Should().Be(JsonSerializer.Serialize(expected, JsonDefaults.Api));
    }

    [Fact]
    public async Task StatsDayDetail_WhenPaused_BuildsLocallyWithoutNetwork()
    {
        var handler = new StatsCountingHandler();
        var harness = new Harness();
        var ct = CancellationToken.None;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var services = new ServiceCollection();
        services.AddSingleton<IBoardDataService>(harness.Board);
        var provider = services.BuildServiceProvider();
        using var offline = new OfflineActivityStatisticsProvider(
            provider,
            new FakeEventStore(),
            NullLogger<OfflineActivityStatisticsProvider>.Instance);
        var remote = new RemoteActivityStatisticsReader(new StubStatsHttpFactory(handler), new StubSettingsStore([]));
        var pauseStore = new StubSettingsStore(new Dictionary<string, string>
        {
            [BoardSyncPausePreference.StoreKey] = "1"
        });
        var reader = new PauseAwareActivityStatisticsReader(remote, offline, pauseStore);

        var detail = await reader.GetActivityDayDetailAsync(today, null, ct);

        handler.HitCount.Should().Be(0);
        detail.Should().NotBeNull();
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T value) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, JsonDefaults.Api), Encoding.UTF8, "application/json")
        };

    private sealed class Harness
    {
        public const string UserKey = "PAUSETEST@LOCAL";

        public Harness()
        {
            Server = new CountingServer();
            Store = new InMemoryBoardLocalStore();
            Status = new BoardSyncStatus();
            var remote = new RemoteBoardDataService(new StubHttpFactory(Server));
            Board = new LocalFirstBoardDataService(
                Store,
                new FakeUserKeys(),
                remote,
                new FakeServices(),
                new FakeTimeZone(),
                Status,
                NullLogger<LocalFirstBoardDataService>.Instance);
            Coordinator = new BoardSyncCoordinator(
                Board,
                new FakeUserKeys(),
                Status,
                new BoardInitialLoadSignal(),
                new RemoteBoardRefreshService(),
                NullLogger<BoardSyncCoordinator>.Instance,
                new FakeServices());
        }

        public CountingServer Server { get; }

        public InMemoryBoardLocalStore Store { get; }

        public BoardSyncStatus Status { get; }

        public LocalFirstBoardDataService Board { get; }

        public BoardSyncCoordinator Coordinator { get; }
    }

    private sealed class CountingServer : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        public int HitCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HitCount++;
            return Task.FromResult(Responder(request));
        }
    }

    private sealed class StubHttpFactory(CountingServer server) : IHttpClientFactory
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

    private sealed class StubSettingsStore(Dictionary<string, string> values) : ILocalSettingsStore
    {
        public Dictionary<string, string> Values { get; } = values;

        public string? Read(string key, string? defaultValue = null) =>
            Values.TryGetValue(key, out var value) ? value : defaultValue;

        public void Write(string key, string value) => Values[key] = value;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int HitCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HitCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubSettingsHttpFactory(CountingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://test.local/") };
    }

    private sealed class FakeSession : IClientSessionProvider
    {
        public bool IsLoggedIn => true;

        public string? Email => "pause@test.local";

#pragma warning disable CS0067
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }

    private sealed class FakeEventStore : IActivityEventStore
    {
        public Task AppendAsync(UserActivityEventRecord record, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<UserActivityEventRecord>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserActivityEventRecord>>([]);

        public Task<IReadOnlyList<UserActivityEventRecord>> GetInRangeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserActivityEventRecord>>([]);

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

#pragma warning disable CS0067
        public event EventHandler<UserActivityEventRecord>? Appended;
#pragma warning restore CS0067
    }

    private sealed class StatsCountingHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        public int HitCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HitCount++;
            return Task.FromResult(Responder(request));
        }
    }

    private sealed class StubStatsHttpFactory(StatsCountingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://test.local/") };
    }

    private sealed class TestSettingsService(
        IHttpClientFactory http,
        IClientSessionProvider session,
        ILocalSettingsStore store,
        ILogger<TestSettingsService> logger)
        : LocalFirstSettingsServiceBase<UserPreferences>(http, session, store, logger,
            new LocalFirstSettingsOptions<UserPreferences>(
                "pause_test_prefs_v1",
                "api/settings/preferences",
                UserPreferencesJson.DeserializeOrDefault,
                UserPreferencesJson.Serialize));
}
