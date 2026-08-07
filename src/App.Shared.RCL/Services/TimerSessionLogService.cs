using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public sealed class TimerSessionLogService(
    GlobalTimerService timer,
    IBoardDataService boardData,
    IUserActivityLogService userActivityLog,
    IUserTimeZoneService timeZoneService,
    IRemoteBoardRefreshService remoteBoardRefresh,
    IClock clock) : ITimerSessionLogService
{
    private readonly IBoardDataService _boardData = boardData;
    private readonly IRemoteBoardRefreshService _remoteBoardRefresh = remoteBoardRefresh;
    private readonly IClock _clock = clock;
    private readonly GlobalTimerService _timer = timer;
    private readonly IUserActivityLogService _userActivityLog = userActivityLog;
    private readonly IUserTimeZoneService _timeZoneService = timeZoneService;

    public async Task<TimerSessionLogResult> LogStoppedSessionAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var targetType = _timer.TargetType;
        var targetId = _timer.TargetId;
        var boardId = _timer.BoardItemId ?? await ResolveBoardItemIdFromTitleAsync(targetType, targetId, cancellationToken)
            .ConfigureAwait(false);
        var boardError = false;

        if (boardId is Guid id && targetType is "Habit" or "Daily" or "Todo")
        {
            try
            {
                var progressed = await UpdateBoardItemProgressAsync(id, targetType, cancellationToken).ConfigureAwait(false);
                if (progressed)
                {
                    _timer.SetManualTarget(null);
                    await _remoteBoardRefresh.NotifyFromRemoteAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                boardError = true;
            }
        }

        try
        {
            var customLabel = boardId is null ? targetId : null;
            await _userActivityLog
                .LogTimerSessionAsync(duration, boardId, customLabel, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Statistics persistence is best-effort; board feedback is primary.
        }

        if (boardError)
        {
            return new TimerSessionLogResult(
                true,
                $"Timer stopped ({duration:hh\\:mm\\:ss}), but the board could not be updated. Check your connection and try again.");
        }

        return new TimerSessionLogResult(
            false,
            $"Timer log saved for {targetType ?? "Unassigned"} '{targetId ?? "-"}' with duration {duration:hh\\:mm\\:ss}.");
    }

    private async Task<bool> UpdateBoardItemProgressAsync(Guid id, string targetType, CancellationToken cancellationToken)
    {
        var snapshot = await _boardData.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (targetType == "Habit")
        {
            var updated = await _boardData.IncrementHabitPlusAsync(id, cancellationToken).ConfigureAwait(false);
            return updated is not null;
        }

        if (targetType == "Daily")
        {
            var daily = snapshot.Dailies.FirstOrDefault(d => d.Id == id);
            var today = DailySchedule.LocalToday(_clock, _timeZoneService);
            if (daily is not null && !DailySchedule.IsCompleteForDate(daily, today))
            {
                var updated = await _boardData.ToggleItemAsync(BoardSection.Daily, id, cancellationToken)
                    .ConfigureAwait(false);
                return updated is not null;
            }
            return false;
        }

        if (targetType == "Todo")
        {
            var todo = snapshot.Todos.FirstOrDefault(t => t.Id == id);
            if (todo is { IsCompleted: false })
            {
                var updated = await _boardData.ToggleItemAsync(BoardSection.Todo, id, cancellationToken)
                    .ConfigureAwait(false);
                return updated is not null;
            }
            return false;
        }

        return false;
    }

    private async Task<Guid?> ResolveBoardItemIdFromTitleAsync(
        string? targetType,
        string? title,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var snapshot = await _boardData.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return targetType switch
        {
            "Habit" => snapshot.Habits.FirstOrDefault(h => string.Equals(h.Title, title, StringComparison.Ordinal))?.Id,
            "Daily" => snapshot.Dailies.FirstOrDefault(d => string.Equals(d.Title, title, StringComparison.Ordinal))?.Id,
            "Todo" => snapshot.Todos.FirstOrDefault(t => string.Equals(t.Title, title, StringComparison.Ordinal))?.Id,
            _ => null
        };
    }
}

