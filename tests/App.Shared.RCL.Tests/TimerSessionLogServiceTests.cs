using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class TimerSessionLogServiceTests
{
    private readonly IBoardDataService _boardData = Substitute.For<IBoardDataService>();
    private readonly IUserActivityLogService _activityLog = Substitute.For<IUserActivityLogService>();
    private readonly IRemoteBoardRefreshService _refresh = Substitute.For<IRemoteBoardRefreshService>();

    [Fact]
    public async Task DailyTarget_AlreadyCompletedToday_DoesNotToggleItOff()
    {
        var id = Guid.NewGuid();
        var daily = new BoardItem(id, "Water", IsCompleted: true, DailyLastCompletedOn: BoardToday());
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [daily], [])));

        var service = CreateService("Daily", "Water", id);

        await service.LogStoppedSessionAsync(TimeSpan.FromMinutes(5));

        await _boardData.DidNotReceive().ToggleItemAsync(
            BoardSection.Daily, id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DailyTarget_OpenToday_TogglesItOnce()
    {
        var id = Guid.NewGuid();
        var daily = new BoardItem(id, "Water", IsCompleted: false);
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [daily], [])));
        _boardData.ToggleItemAsync(BoardSection.Daily, id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(daily with { IsCompleted = true, DailyLastCompletedOn = BoardToday() }));

        var service = CreateService("Daily", "Water", id);

        await service.LogStoppedSessionAsync(TimeSpan.FromMinutes(5));

        await _boardData.Received(1).ToggleItemAsync(
            BoardSection.Daily, id, Arg.Any<CancellationToken>());
    }

    private TimerSessionLogService CreateService(string targetType, string title, Guid id)
    {
        var timer = new GlobalTimerService(new SystemClock());
        timer.SelectTarget(targetType, title, id);
        return new TimerSessionLogService(timer, _boardData, _activityLog, _refresh);
    }

    private static DateOnly BoardToday() => DateOnly.FromDateTime(DateTime.UtcNow);
}
