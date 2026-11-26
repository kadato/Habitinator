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

    [Fact]
    public async Task SnapshotAsync_StaleHabitAnchor_ShowsZeroWithoutWipingStore()
    {
        var harness = new Harness();
        var ct = CancellationToken.None;
        var item = await harness.Board.CreateItemAsync(BoardSection.Habit, "Read", cancellationToken: ct);
        await harness.Board.IncrementHabitPlusAsync(item.Id, ct);

        var row = await harness.Store.FindItemAsync(Harness.UserKey, item.Id, ct);
        Assert.NotNull(row);
        row.Counter = 5;
        row.HabitPeriodStart = HabitResetSchedule.PeriodStartFor(
            DateOnly.FromDateTime(DateTime.UtcNow), HabitResetPeriod.Daily).AddDays(-1);
        await harness.Store.UpsertItemAsync(row, ct);

        var snapshot = await harness.Board.GetSnapshotAsync(ct);
        snapshot.Habits.Should().ContainSingle(x => x.Id == item.Id && x.Counter == 0);
    }

    [Fact]
    public async Task IncrementHabitPlusAsync_StaleAnchor_ResetsThenCountsOne()
    {
        var harness = new Harness();
        var ct = CancellationToken.None;
        var item = await harness.Board.CreateItemAsync(BoardSection.Habit, "Read", cancellationToken: ct);

        var row = await harness.Store.FindItemAsync(Harness.UserKey, item.Id, ct);
        Assert.NotNull(row);
        row.Counter = 5;
        row.NegativeCounter = 3;
        row.HabitPeriodStart = HabitResetSchedule.PeriodStartFor(
            DateOnly.FromDateTime(DateTime.UtcNow), HabitResetPeriod.Daily).AddDays(-1);
        await harness.Store.UpsertItemAsync(row, ct);

        var updated = await harness.Board.IncrementHabitPlusAsync(item.Id, ct);

        Assert.NotNull(updated);
        updated.Counter.Should().Be(1);
        updated.NegativeCounter.Should().Be(0);
    }

    [Fact]
    public async Task GetItemAsync_FetchesRemoteWhenMirrorEmpty()
    {
        var ct = CancellationToken.None;
        var itemId = Guid.NewGuid();
        var daily = new BoardItem(itemId, "Water", DailyStartDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3));
        var harness = new Harness(new SnapshotHandler(new BoardSnapshot([], [daily], [])));

        var found = await harness.Board.GetItemAsync(itemId, ct);

        Assert.NotNull(found);
        found.Title.Should().Be("Water");
    }

    [Fact]
    public async Task GetItemAsync_ReturnsNullForUnknownIdWithoutNetwork()
    {
        var harness = new Harness();
        var ct = CancellationToken.None;

        var found = await harness.Board.GetItemAsync(Guid.NewGuid(), ct);

        found.Should().BeNull();
    }

    [Fact]
    public async Task SnapshotAsync_DailyCompletedYesterday_ShowsOpenToday()
    {
        var harness = new Harness();
        var ct = CancellationToken.None;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var id = Guid.NewGuid();
        await harness.Store.UpsertItemAsync(new BoardLocalRow
        {
            Id = id,
            UserKey = Harness.UserKey,
            Section = BoardSection.Daily,
            Title = "Water",
            IsCompleted = true,
            DailyStartDate = today.AddDays(-10),
            DailyLastCompletedOn = today.AddDays(-1)
        }, ct);

        var snapshot = await harness.Board.GetSnapshotAsync(ct);
        var item = await harness.Board.GetItemAsync(id, ct);

        snapshot.Dailies.Should().ContainSingle(x => x.Id == id && !x.IsCompleted);
        Assert.NotNull(item);
        item.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task SnapshotAsync_DailySubtaskCheckedYesterday_ShowsOpenToday()
    {
        var harness = new Harness();
        var ct = CancellationToken.None;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rowId = Guid.NewGuid();
        var checklist = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(rowId, "Warm up", IsDone: true, DoneOn: today.AddDays(-1)),
        ]);
        await harness.Store.UpsertItemAsync(new BoardLocalRow
        {
            Id = Guid.NewGuid(),
            UserKey = Harness.UserKey,
            Section = BoardSection.Daily,
            Title = "Run",
            ChecklistJson = checklist,
            DailyStartDate = today.AddDays(-10)
        }, ct);

        var snapshot = await harness.Board.GetSnapshotAsync(ct);

        var item = snapshot.Dailies.Should().ContainSingle().Subject;
        DailyChecklistJson.Parse(item.ChecklistJson).Should().ContainSingle().Which.IsDone.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteDailyForDateAsync_Yesterday_LeavesTodayOpen()
    {
        var harness = new Harness();
        var ct = CancellationToken.None;
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var daily = await harness.Board.CreateItemAsync(BoardSection.Daily, "Water", cancellationToken: ct);

        var updated = await harness.Board.CompleteDailyForDateAsync(daily.Id, yesterday, ct);
        var snapshot = await harness.Board.GetSnapshotAsync(ct);

        Assert.NotNull(updated);
        updated.DailyLastCompletedOn.Should().Be(yesterday);
        updated.IsCompleted.Should().BeFalse();
        snapshot.Dailies.Should().ContainSingle(x => x.Id == daily.Id
            && x.DailyLastCompletedOn == yesterday
            && !x.IsCompleted);
    }

    [Fact]
    public async Task SkipDailyForDateAsync_AllowsToday()
    {
        var userDay = new TestUserDayService { Today = new DateOnly(2026, 8, 12) };
        var harness = new Harness(userDay: userDay);
        var ct = CancellationToken.None;
        var daily = await harness.Board.CreateItemAsync(BoardSection.Daily, "Water", cancellationToken: ct);

        var updated = await harness.Board.SkipDailyForDateAsync(daily.Id, userDay.Today, ct);

        Assert.NotNull(updated);
        updated.IsCompleted.Should().BeFalse();
        updated.DailySkippedOn.Should().Be(userDay.Today);
        DailySchedule.IsDueOnDate(updated, userDay.Today).Should().BeFalse();
        var snapshot = await harness.Board.GetSnapshotAsync(ct);
        snapshot.Dailies.Should().ContainSingle(x => x.Id == daily.Id && !x.IsCompleted);
        (await harness.Store.ListOutboxAsync(Harness.UserKey))
            .Should().Contain(x => x.Kind == BoardOutboxOperationKind.SkipDailyForDate);
    }

    [Fact]
    public async Task SkipDailyForDateAsync_RejectsFutureDays()
    {
        var userDay = new TestUserDayService { Today = new DateOnly(2026, 8, 12) };
        var harness = new Harness(userDay: userDay);
        var ct = CancellationToken.None;
        var daily = await harness.Board.CreateItemAsync(BoardSection.Daily, "Water", cancellationToken: ct);

        var updated = await harness.Board.SkipDailyForDateAsync(daily.Id, userDay.Today.AddDays(1), ct);

        updated.Should().BeNull();
    }

    [Fact]
    public async Task UpdateHabitAsync_PersistsTargetCount()
    {
        var harness = new Harness();
        var ct = CancellationToken.None;
        var habit = await harness.Board.CreateItemAsync(BoardSection.Habit, "Water", cancellationToken: ct);

        var updated = await harness.Board.UpdateHabitAsync(
            habit.Id,
            UpdateHabitArgs.From(habit) with { TargetCount = 8 },
            ct);

        Assert.NotNull(updated);
        updated.TargetCount.Should().Be(8);
        var snapshot = await harness.Board.GetSnapshotAsync(ct);
        snapshot.Habits.Should().ContainSingle(x => x.Id == habit.Id && x.TargetCount == 8);
        var outbox = await harness.Store.ListOutboxAsync(Harness.UserKey);
        var payload = System.Text.Json.JsonSerializer.Deserialize<UpdateHabitOutboxPayload>(
            outbox.Should().ContainSingle(x => x.Kind == BoardOutboxOperationKind.UpdateHabit).Subject.PayloadJson,
            JsonDefaults.Api);
        payload!.TargetCount.Should().Be(8);
    }

    [Fact]
    public async Task PostponeDailyAsync_MovesTodaysOccurrenceToTomorrow()
    {
        var userDay = new TestUserDayService { Today = new DateOnly(2026, 8, 12) };
        var harness = new Harness(userDay: userDay);
        var ct = CancellationToken.None;
        var tomorrow = userDay.Today.AddDays(1);
        var daily = await harness.Board.CreateItemAsync(BoardSection.Daily, "Water", cancellationToken: ct);

        var updated = await harness.Board.PostponeDailyAsync(daily.Id, tomorrow, ct);

        Assert.NotNull(updated);
        updated.DailyPostponedUntil.Should().Be(tomorrow);
        var snapshot = await harness.Board.GetSnapshotAsync(ct);
        var item = snapshot.Dailies.Should().ContainSingle(x => x.Id == daily.Id).Subject;
        item.DailyPostponedUntil.Should().Be(tomorrow);
        DailySchedule.IsDueOnDate(item, userDay.Today).Should().BeFalse();
        DailySchedule.IsDueOnDate(item, tomorrow).Should().BeTrue();
        (await harness.Store.ListOutboxAsync(Harness.UserKey))
            .Should().Contain(x => x.Kind == BoardOutboxOperationKind.PostponeDaily);
    }

    [Fact]
    public async Task PostponeDailyAsync_Twice_KeepsFirstMovedFromDay()
    {
        var userDay = new TestUserDayService { Today = new DateOnly(2026, 8, 10) };
        var harness = new Harness(userDay: userDay);
        var ct = CancellationToken.None;
        var monday = userDay.Today;
        var daily = await harness.Board.CreateItemAsync(BoardSection.Daily, "Water", cancellationToken: ct);
        await harness.Board.PostponeDailyAsync(daily.Id, monday.AddDays(1), ct);

        userDay.Today = monday.AddDays(1);
        var updated = await harness.Board.PostponeDailyAsync(daily.Id, monday.AddDays(2), ct);

        Assert.NotNull(updated);
        updated.DailyPostponedFrom.Should().Be(monday);
        updated.DailyPostponedUntil.Should().Be(monday.AddDays(2));
    }

    [Fact]
    public async Task PostponeDailyAsync_RejectsDatesOtherThanTomorrow()
    {
        var userDay = new TestUserDayService { Today = new DateOnly(2026, 8, 12) };
        var harness = new Harness(userDay: userDay);
        var ct = CancellationToken.None;
        var daily = await harness.Board.CreateItemAsync(BoardSection.Daily, "Water", cancellationToken: ct);

        var updated = await harness.Board.PostponeDailyAsync(daily.Id, userDay.Today.AddDays(2), ct);

        updated.Should().BeNull();
        var row = await harness.Store.FindItemAsync(Harness.UserKey, daily.Id, ct);
        Assert.NotNull(row);
        row.DailyPostponedUntil.Should().BeNull();
    }

    [Fact]
    public async Task TryGetCachedSnapshot_DoesNotReuseYesterdaysSnapshot()
    {
        var userDay = new TestUserDayService { Today = DateOnly.FromDateTime(DateTime.UtcNow) };
        var harness = new Harness(userDay: userDay);
        var ct = CancellationToken.None;
        var today = userDay.Today;
        var id = Guid.NewGuid();
        await harness.Store.UpsertItemAsync(new BoardLocalRow
        {
            Id = id,
            UserKey = Harness.UserKey,
            Section = BoardSection.Daily,
            Title = "Water",
            IsCompleted = true,
            DailyStartDate = today.AddDays(-10),
            DailyLastCompletedOn = today
        }, ct);

        var snapshot = await harness.Board.GetSnapshotAsync(ct);
        harness.Board.TryGetCachedSnapshot(out var cached).Should().BeTrue();

        snapshot.Dailies.Should().ContainSingle(x => x.Id == id && x.IsCompleted);
        Assert.NotNull(cached);
        cached.Dailies.Should().ContainSingle(x => x.Id == id && x.IsCompleted);

        userDay.Today = today.AddDays(1);

        harness.Board.TryGetCachedSnapshot(out _).Should().BeFalse();
    }

    private sealed class Harness
    {
        public const string UserKey = "WEBTEST@LOCAL";

        public Harness(HttpMessageHandler? handler = null, IUserDayService? userDay = null)
        {
            Users = new FakeUserKeys();
            Store = new InMemoryBoardLocalStore();
            var remote = new RemoteBoardDataService(new FixedHttpFactory(handler ?? new FailingHandler()));
            var services = new FakeServices();
            Board = new LocalFirstBoardDataService(
                Store,
                Users,
                remote,
                services,
                userDay ?? new TestUserDayService(),
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

    private sealed class FixedHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("http://localhost/") };
    }

    private sealed class SnapshotHandler(BoardSnapshot snapshot) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/api/board", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(snapshot, options: JsonDefaults.Api)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}
