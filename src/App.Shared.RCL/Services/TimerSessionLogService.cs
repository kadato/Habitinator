using App.Shared.RCL.Models;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

public sealed class TimerSessionLogService(
    GlobalTimerService timer,
    IBoardDataService boardData,
    IUserActivityLogService userActivityLog,
    IRemoteBoardRefreshService remoteBoardRefresh,
    IActivityStatisticsReader? statsReader = null,
    ILogger<TimerSessionLogService>? logger = null) : ITimerSessionLogService
{
    public async Task<TimerSessionLogResult> LogStoppedSessionAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var targetType = timer.TargetType;
        var targetId = timer.TargetId;
        var boardId = timer.BoardItemId ?? await ResolveBoardItemIdFromTitleAsync(targetType, targetId, cancellationToken)
            .ConfigureAwait(false);
        var boardError = false;
        var boardProgressed = false;

        if (boardId is Guid id && targetType is "Habit" or "Daily" or "Todo")
        {
            try
            {
                boardProgressed = await UpdateBoardItemProgressAsync(id, targetType, cancellationToken).ConfigureAwait(false);
                if (boardProgressed)
                {
                    timer.SetManualTarget(null);
                    // The board write is local-first and already queued a sync.
                    // Refresh other subscribers in the background so stopping
                    // the timer never waits on the refresh.
                    _ = NotifyRefreshInBackgroundAsync();
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Updating the timer target on the board failed.");
                boardError = true;
            }
        }

        try
        {
            var customLabel = boardId is null ? targetId : null;
            await userActivityLog
                .LogTimerSessionAsync(duration, boardId, customLabel, cancellationToken)
                .ConfigureAwait(false);
            statsReader?.InvalidateCache();
        }
        catch (Exception ex)
        {
            // Statistics persistence is best-effort. Board feedback is primary.
            logger?.LogDebug(ex, "Persisting the timer session activity failed.");
        }

        if (boardError)
        {
            return new TimerSessionLogResult(
                true,
                boardProgressed,
                $"Timer stopped ({duration:hh\\:mm\\:ss}), but Habitinator could not update the board. Try again.");
        }

        if (string.IsNullOrWhiteSpace(targetId))
        {
            return new TimerSessionLogResult(
                false,
                false,
                $"The timer logged {duration:hh\\:mm\\:ss} of focus time. You picked no board target.");
        }

        return new TimerSessionLogResult(
            false,
            boardProgressed,
            $"The timer saved {targetType ?? "Unassigned"} '{targetId}' with duration {duration:hh\\:mm\\:ss}.");
    }

    private async Task NotifyRefreshInBackgroundAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await remoteBoardRefresh.NotifyFromRemoteAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The periodic sync and the error toast already cover failures.
            logger?.LogDebug(ex, "Notifying board refresh after a timer log failed.");
        }
    }

    private async Task<bool> UpdateBoardItemProgressAsync(Guid id, string targetType, CancellationToken cancellationToken)
    {
        var snapshot = await boardData.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (targetType == "Habit")
        {
            var updated = await boardData.IncrementHabitPlusAsync(id, cancellationToken).ConfigureAwait(false);
            return updated is not null;
        }

        if (targetType == "Daily")
        {
            var daily = snapshot.Dailies.FirstOrDefault(d => d.Id == id);
            if (daily is { IsCompleted: false })
            {
                var updated = await boardData.ToggleItemAsync(BoardSection.Daily, id, cancellationToken)
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
                var updated = await boardData.ToggleItemAsync(BoardSection.Todo, id, cancellationToken)
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

        var snapshot = await boardData.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return targetType switch
        {
            "Habit" => snapshot.Habits.FirstOrDefault(h => string.Equals(h.Title, title, StringComparison.Ordinal))?.Id,
            "Daily" => snapshot.Dailies.FirstOrDefault(d => string.Equals(d.Title, title, StringComparison.Ordinal))?.Id,
            "Todo" => snapshot.Todos.FirstOrDefault(t => string.Equals(t.Title, title, StringComparison.Ordinal))?.Id,
            _ => null
        };
    }
}

