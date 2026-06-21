using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>Wraps a board data service and records the same activity events as the web persistence layer.</summary>
public sealed class ActivityLoggingBoardDataService : IBoardDataService
{
    private readonly IUserActivityLogService _activityLog;
    private readonly IBoardDataService _inner;

    public ActivityLoggingBoardDataService(IBoardDataService inner, IUserActivityLogService activityLog)
    {
        _inner = inner;
        _activityLog = activityLog;
    }

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
        var updated = await _inner.CompleteDailyForDateAsync(itemId, completedOn, cancellationToken);
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

        var snap = await _inner.GetSnapshotAsync(cancellationToken);
        var before = section == BoardSection.Daily
            ? snap.Dailies.FirstOrDefault(x => x.Id == itemId)
            : snap.Todos.FirstOrDefault(x => x.Id == itemId);
        if (before is null)
        {
            return await _inner.ToggleItemAsync(section, itemId, cancellationToken);
        }

        var today = DateOnly.FromDateTime(DateTime.Now); // Use local timezone for consistency
        var wasComplete = section == BoardSection.Daily
            ? before.DailyLastCompletedOn == today || (before.DailyLastCompletedOn is null && before.IsCompleted)
            : before.IsCompleted;

        var updated = await _inner.ToggleItemAsync(section, itemId, cancellationToken);
        if (updated is not null)
        {
            var type = section == BoardSection.Daily
                ? wasComplete ? ActivityEventType.DailyUncomplete : ActivityEventType.DailyComplete
                : wasComplete
                    ? ActivityEventType.TodoUncomplete
                    : ActivityEventType.TodoComplete;
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
        var updated = await _inner.IncrementHabitPlusAsync(itemId, cancellationToken);
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
            }
        }

        return updated;
    }

    public async Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var updated = await _inner.IncrementHabitMinusAsync(itemId, cancellationToken);
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
            }
        }

        return updated;
    }

    public Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        bool trackPlus,
        bool trackMinus,
        HabitResetPeriod resetPeriod,
        int counter,
        int negativeCounter,
        string? checklistJson = null,
        double? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.UpdateHabitAsync(
            itemId,
            title,
            notes,
            tags,
            trackPlus,
            trackMinus,
            resetPeriod,
            counter,
            negativeCounter,
            checklistJson,
            sortOrder,
            cancellationToken);
    }

    public Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        string? checklistJson,
        DateTime? dueDate,
        double? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.UpdateTodoAsync(itemId, title, notes, tags, checklistJson, dueDate, sortOrder, cancellationToken);
    }

    public Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        DateTime? startDate,
        DailyRepeatType repeatType,
        int repeatInterval,
        string? checklistJson,
        int streak,
        double? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.UpdateDailyAsync(
            itemId,
            title,
            notes,
            tags,
            startDate,
            repeatType,
            repeatInterval,
            checklistJson,
            streak,
            sortOrder,
            cancellationToken);
    }
}
