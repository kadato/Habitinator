using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>Wraps a board data service and records the same activity events as the web persistence layer.</summary>
public sealed class ActivityLoggingBoardDataService(IBoardDataService inner, IUserActivityLogService activityLog) : IBoardDataService
{
    private readonly IUserActivityLogService _activityLog = activityLog;
    private readonly IBoardDataService _inner = inner;

    public Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetSnapshotAsync(cancellationToken);
    }

    public Task<BoardItem> CreateItemAsync(BoardSection section, string title, Guid? itemId = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.CreateItemAsync(section, title, itemId, cancellationToken);
    }

    public Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title,
        CancellationToken cancellationToken = default)
    {
        return _inner.RenameItemAsync(section, itemId, title, cancellationToken);
    }

    public Task<bool> DeleteItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        return _inner.DeleteItemAsync(section, itemId, cancellationToken);
    }

    public Task<BoardItem?> ArchiveItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        return _inner.ArchiveItemAsync(section, itemId, cancellationToken);
    }

    public Task<BoardItem?> UnarchiveItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        return _inner.UnarchiveItemAsync(section, itemId, cancellationToken);
    }

    public Task<BoardSnapshot> GetArchivedSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetArchivedSnapshotAsync(cancellationToken);
    }

    public async Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn,
        CancellationToken cancellationToken = default)
    {
        BoardItem? updated = await _inner.CompleteDailyForDateAsync(itemId, completedOn, cancellationToken);
        if (updated is not null)
        {
            try
            {
                await _activityLog.LogActivityAsync(
                    ActivityEventType.DailyComplete,
                    itemId,
                    itemTitleSnapshot: updated.Title,
                    cancellationToken: cancellationToken);
            }
            catch (Exception)
            {
                // Log failure is non-critical, proceed with the UI action
            }
        }

        return updated;
    }

    public async Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        if (section == BoardSection.Habit)
        {
            return await _inner.ToggleItemAsync(section, itemId, cancellationToken);
        }

        BoardSnapshot snap = await _inner.GetSnapshotAsync(cancellationToken);
        BoardItem? before = section == BoardSection.Daily
            ? snap.Dailies.FirstOrDefault(x => x.Id == itemId)
            : snap.Todos.FirstOrDefault(x => x.Id == itemId);
        if (before is null)
        {
            return await _inner.ToggleItemAsync(section, itemId, cancellationToken);
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.Now); // Use local timezone for consistency
        bool wasComplete = section == BoardSection.Daily
            ? before.DailyLastCompletedOn == today || (before.DailyLastCompletedOn is null && before.IsCompleted)
            : before.IsCompleted;

        BoardItem? updated = await _inner.ToggleItemAsync(section, itemId, cancellationToken);
        if (updated is not null)
        {
            ActivityEventType type = (section, wasComplete) switch
            {
                (BoardSection.Daily, true) => ActivityEventType.DailyUncomplete,
                (BoardSection.Daily, false) => ActivityEventType.DailyComplete,
                (_, true) => ActivityEventType.TodoUncomplete,
                (_, false) => ActivityEventType.TodoComplete
            };
            try
            {
                await _activityLog.LogActivityAsync(type, itemId, itemTitleSnapshot: updated.Title, cancellationToken: cancellationToken);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        return updated;
    }

    public async Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItem? updated = await _inner.IncrementHabitPlusAsync(itemId, cancellationToken);
        if (updated is not null)
        {
            try
            {
                await _activityLog.LogActivityAsync(
                    ActivityEventType.HabitPlus,
                    itemId,
                    itemTitleSnapshot: updated.Title,
                    cancellationToken: cancellationToken);
            }
            catch (Exception)
            {
                // Log failure is non-critical, proceed with the UI action
            }
        }

        return updated;
    }

    public async Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItem? updated = await _inner.IncrementHabitMinusAsync(itemId, cancellationToken);
        if (updated is not null)
        {
            try
            {
                await _activityLog.LogActivityAsync(
                    ActivityEventType.HabitMinus,
                    itemId,
                    itemTitleSnapshot: updated.Title,
                    cancellationToken: cancellationToken);
            }
            catch (Exception)
            {
                // Log failure is non-critical, proceed with the UI action
            }
        }

        return updated;
    }

    public Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        UpdateHabitArgs args,
        CancellationToken cancellationToken = default)
    {
        return _inner.UpdateHabitAsync(
            itemId,
            args,
            cancellationToken);
    }

    public Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        UpdateTodoArgs args,
        CancellationToken cancellationToken = default)
    {
        return _inner.UpdateTodoAsync(itemId, args, cancellationToken);
    }

    public Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        UpdateDailyArgs args,
        CancellationToken cancellationToken = default)
    {
        return _inner.UpdateDailyAsync(
            itemId,
            args,
            cancellationToken);
    }
}
