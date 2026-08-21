#pragma warning disable IDE0005 // IDE0005 for JsonContent global using false positive where analyzer flags System.Net.Http.Json as unnecessary though required
using System.Net;
using System.Net.Http.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Remote;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class StatsCacheInvalidationTests
{
    private static IHttpClientFactory CreateFactory(HttpResponseMessage response)
    {
        var mockHandler = new MockHttpMessageHandler(response);
        var client = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        return factory;
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public MockHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(_response);
    }

    private static BoardItem SampleItem(BoardSection section, Guid id, string title = "Test")
    {
        _ = section;
        return new(id, title, IsCompleted: false, Counter: 0, Tags: null, TrackPlus: true, TrackMinus: true, ServerUpdatedAtUtc: DateTimeOffset.UtcNow, CreatedAtUtc: DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateItem_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var item = SampleItem(BoardSection.Habit, Guid.NewGuid());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(item, options: JsonDefaults.Api) };
        var factory = CreateFactory(response);
        var service = new RemoteBoardDataService(factory, null, stats);

        await service.CreateItemAsync(BoardSection.Habit, "Test", item.Id, Guid.NewGuid());

        stats.Received(1).InvalidateCache();
    }

    [Fact]
    public async Task ToggleDaily_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var item = SampleItem(BoardSection.Daily, Guid.NewGuid());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(item, options: JsonDefaults.Api) };
        var factory = CreateFactory(response);
        var service = new RemoteBoardDataService(factory, null, stats);

        await service.ToggleItemAsync(BoardSection.Daily, item.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);

        stats.Received(1).InvalidateCache();
    }

    [Fact]
    public async Task ToggleTodo_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var item = SampleItem(BoardSection.Todo, Guid.NewGuid());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(item, options: JsonDefaults.Api) };
        var factory = CreateFactory(response);
        var service = new RemoteBoardDataService(factory, null, stats);

        await service.ToggleItemAsync(BoardSection.Todo, item.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);

        stats.Received(1).InvalidateCache();
    }

    [Fact]
    public async Task CompleteDailyForDate_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var item = SampleItem(BoardSection.Daily, Guid.NewGuid());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(item, options: JsonDefaults.Api) };
        var factory = CreateFactory(response);
        var service = new RemoteBoardDataService(factory, null, stats);

        await service.CompleteDailyForDateAsync(item.Id, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid(), DateTimeOffset.UtcNow);

        stats.Received(1).InvalidateCache();
    }

    [Fact]
    public async Task IncrementHabitPlus_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var item = SampleItem(BoardSection.Habit, Guid.NewGuid());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(item, options: JsonDefaults.Api) };
        var factory = CreateFactory(response);
        var service = new RemoteBoardDataService(factory, null, stats);

        await service.IncrementHabitPlusAsync(item.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);

        stats.Received(1).InvalidateCache();
    }

    [Fact]
    public async Task IncrementHabitMinus_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var item = SampleItem(BoardSection.Habit, Guid.NewGuid());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(item, options: JsonDefaults.Api) };
        var factory = CreateFactory(response);
        var service = new RemoteBoardDataService(factory, null, stats);

        await service.IncrementHabitMinusAsync(item.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);

        stats.Received(1).InvalidateCache();
    }

    [Fact]
    public async Task UpdateHabit_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var item = SampleItem(BoardSection.Habit, Guid.NewGuid());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(item, options: JsonDefaults.Api) };
        var factory = CreateFactory(response);
        var service = new RemoteBoardDataService(factory, null, stats);

        await service.UpdateHabitAsync(item.Id, new UpdateHabitArgs("New", null, null, true, true, HabitResetPeriod.Daily, 1, 0, null, null), Guid.NewGuid(), DateTimeOffset.UtcNow);

        stats.Received(1).InvalidateCache();
    }

    [Fact]
    public async Task Archive_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var item = SampleItem(BoardSection.Habit, Guid.NewGuid());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(item, options: JsonDefaults.Api) };
        var factory = CreateFactory(response);
        var service = new RemoteBoardDataService(factory, null, stats);

        await service.ArchiveItemAsync(BoardSection.Habit, item.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);

        stats.Received(1).InvalidateCache();
    }

    [Fact]
    public async Task Delete_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var factory = CreateFactory(response);
        var service = new RemoteBoardDataService(factory, null, stats);

        await service.DeleteItemAsync(BoardSection.Todo, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        stats.Received(1).InvalidateCache();
    }

    [Fact]
    public async Task Unarchive_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var item = SampleItem(BoardSection.Daily, Guid.NewGuid());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(item, options: JsonDefaults.Api) };
        var factory = CreateFactory(response);
        var service = new RemoteBoardDataService(factory, null, stats);

        await service.UnarchiveItemAsync(BoardSection.Daily, item.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);

        stats.Received(1).InvalidateCache();
    }

    [Fact]
    public async Task UpdateDaily_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var item = SampleItem(BoardSection.Daily, Guid.NewGuid());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(item, options: JsonDefaults.Api) };
        var factory = CreateFactory(response);
        var service = new RemoteBoardDataService(factory, null, stats);

        await service.UpdateDailyAsync(item.Id, new UpdateDailyArgs("New Daily", null, null, DateOnly.FromDateTime(DateTime.UtcNow), DailyRepeatType.Daily, 1, null, 0, null), Guid.NewGuid(), DateTimeOffset.UtcNow);

        stats.Received(1).InvalidateCache();
    }

    [Fact]
    public async Task UpdateTodo_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var item = SampleItem(BoardSection.Todo, Guid.NewGuid());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(item, options: JsonDefaults.Api) };
        var factory = CreateFactory(response);
        var service = new RemoteBoardDataService(factory, null, stats);

        await service.UpdateTodoAsync(item.Id, new UpdateTodoArgs("New Todo", null, null, null, null, null, null), Guid.NewGuid(), DateTimeOffset.UtcNow);

        stats.Received(1).InvalidateCache();
    }

    [Fact]
    public async Task TimerSession_Pomodoro_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var boardData = Substitute.For<IBoardDataService>();
        boardData.GetSnapshotAsync(Arg.Any<CancellationToken>()).Returns(new BoardSnapshot([], [], []));
        var activityLog = Substitute.For<IUserActivityLogService>();
        var timeZone = Substitute.For<IUserTimeZoneService>();
        var remoteRefresh = Substitute.For<IRemoteBoardRefreshService>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var timer = new GlobalTimerService(clock);
        timer.SelectTarget("Habit", "Test Habit", Guid.NewGuid());
        var service = new TimerSessionLogService(timer, boardData, activityLog, timeZone, remoteRefresh, clock, stats);

        await service.LogStoppedSessionAsync(TimeSpan.FromMinutes(25));

        // Timer path invalidates via activity log and directly
        await activityLog.Received().LogTimerSessionAsync(Arg.Any<TimeSpan>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        stats.Received().InvalidateCache();
    }

    [Fact]
    public async Task TimerSession_Stopwatch_InvalidatesStatsCache()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var boardData = Substitute.For<IBoardDataService>();
        boardData.GetSnapshotAsync(Arg.Any<CancellationToken>()).Returns(new BoardSnapshot([], [], []));
        var activityLog = Substitute.For<IUserActivityLogService>();
        var timeZone = Substitute.For<IUserTimeZoneService>();
        var remoteRefresh = Substitute.For<IRemoteBoardRefreshService>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var timer = new GlobalTimerService(clock);
        // No target, stopwatch mode
        var service = new TimerSessionLogService(timer, boardData, activityLog, timeZone, remoteRefresh, clock, stats);

        await service.LogStoppedSessionAsync(TimeSpan.FromMinutes(5));

        await activityLog.Received().LogTimerSessionAsync(Arg.Any<TimeSpan>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        stats.Received().InvalidateCache();
    }

    [Fact]
    public async Task Undo_InvalidatesStatsCacheViaBoard()
    {
        var stats = Substitute.For<IActivityStatisticsReader>();
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(SampleItem(BoardSection.Habit, Guid.NewGuid()), options: JsonDefaults.Api) });
        var board = new RemoteBoardDataService(factory, null, stats);
        var undo = new UndoService(Substitute.For<MudBlazor.ISnackbar>(), Substitute.For<INotificationSettingsService>(), Substitute.For<INotificationSettingsRules>(), NullLogger<UndoService>.Instance);
        // Register an undo that does a board increment which invalidates
        var itemId = Guid.NewGuid();
        undo.RegisterUndo("Test", async () => { await board.IncrementHabitPlusAsync(itemId, Guid.NewGuid(), DateTimeOffset.UtcNow); });

        await undo.UndoAsync();

        stats.Received().InvalidateCache();
    }
}

