using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public sealed class UndoableBoardDataService(IBoardDataService inner, IUndoService undoService) : IBoardDataService
{
    private readonly IBoardDataService _inner = inner;
    private readonly IUndoService _undoService = undoService;

    public Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetSnapshotAsync(cancellationToken);
    }

    public async Task<BoardItem?> ArchiveItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItem? item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.ArchiveItemAsync(section, itemId, cancellationToken);
        }

        BoardItem? result = await _inner.ArchiveItemAsync(section, itemId, cancellationToken);
        if (result is not null && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Archive \"{item.Title}\"", async () =>
            {
                await _inner.UnarchiveItemAsync(section, itemId, CancellationToken.None);
            });
        }
        return result;
    }

    public async Task<BoardItem?> UnarchiveItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItem? result = await _inner.UnarchiveItemAsync(section, itemId, cancellationToken);
        if (result is not null && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Unarchive \"{result.Title}\"", async () =>
            {
                await _inner.ArchiveItemAsync(section, itemId, CancellationToken.None);
            });
        }
        return result;
    }

    public Task<BoardSnapshot> GetArchivedSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetArchivedSnapshotAsync(cancellationToken);
    }

    public async Task<BoardItem> CreateItemAsync(BoardSection section, string title, Guid? itemId = null, CancellationToken cancellationToken = default)
    {
        BoardItem item = await _inner.CreateItemAsync(section, title, itemId, cancellationToken);
        if (!_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Add \"{item.Title}\"", async () =>
            {
                await _inner.DeleteItemAsync(section, item.Id, CancellationToken.None);
            });
        }
        return item;
    }

    public async Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title, CancellationToken cancellationToken = default)
    {
        BoardItem? item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.RenameItemAsync(section, itemId, title, cancellationToken);
        }

        string oldTitle = item.Title;
        BoardItem? result = await _inner.RenameItemAsync(section, itemId, title, cancellationToken);
        if (result is not null && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Rename \"{oldTitle}\" to \"{result.Title}\"", async () =>
            {
                await _inner.RenameItemAsync(section, itemId, oldTitle, CancellationToken.None);
            });
        }
        return result;
    }

    public async Task<bool> DeleteItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItem? item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.DeleteItemAsync(section, itemId, cancellationToken);
        }

        bool success = await _inner.DeleteItemAsync(section, itemId, cancellationToken);
        if (success && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Delete \"{item.Title}\"", async () =>
            {
                await RestoreDeletedItemAsync(section, item).ConfigureAwait(false);
            });
        }
        return success;
    }

    private async Task RestoreDeletedItemAsync(BoardSection section, BoardItem item)
    {
        BoardItem recreated = await _inner.CreateItemAsync(section, item.Title, item.Id, CancellationToken.None).ConfigureAwait(false);
        if (section == BoardSection.Habit)
        {
            await _inner.UpdateHabitAsync(
                recreated.Id,
                new UpdateHabitArgs(
                    item.Title,
                    item.Notes,
                    item.Tags,
                    item.TrackPlus,
                    item.TrackMinus,
                    item.ResetPeriod,
                    item.Counter,
                    item.NegativeCounter,
                    item.ChecklistJson,
                    item.SortOrder),
                CancellationToken.None).ConfigureAwait(false);
        }
        else if (section == BoardSection.Daily)
        {
            await _inner.UpdateDailyAsync(
                recreated.Id,
                new UpdateDailyArgs(
                    item.Title,
                    item.Notes,
                    item.Tags,
                    item.DailyStartDate?.ToDateTime(TimeOnly.MinValue),
                    item.DailyRepeat,
                    item.DailyRepeatInterval,
                    item.ChecklistJson,
                    item.Counter,
                    item.SortOrder),
                CancellationToken.None).ConfigureAwait(false);
            if (item.IsCompleted)
            {
                await _inner.ToggleItemAsync(BoardSection.Daily, recreated.Id, CancellationToken.None).ConfigureAwait(false);
            }
        }
        else if (section == BoardSection.Todo)
        {
            await _inner.UpdateTodoAsync(
                recreated.Id,
                new UpdateTodoArgs(
                    item.Title,
                    item.Notes,
                    item.Tags,
                    item.ChecklistJson,
                    item.TodoDueDate?.ToDateTime(TimeOnly.MinValue),
                    item.SortOrder),
                CancellationToken.None).ConfigureAwait(false);
            if (item.IsCompleted)
            {
                await _inner.ToggleItemAsync(BoardSection.Todo, recreated.Id, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public async Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItem? result = await _inner.ToggleItemAsync(section, itemId, cancellationToken);
        if (result is not null && !_undoService.IsUndoing)
        {
            string actionVerb = result.IsCompleted ? "Complete" : "Uncomplete";
            _undoService.RegisterUndo($"{actionVerb} \"{result.Title}\"", async () =>
            {
                await _inner.ToggleItemAsync(section, itemId, CancellationToken.None);
            });
        }
        return result;
    }

    public Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn, CancellationToken cancellationToken = default)
    {
        return _inner.CompleteDailyForDateAsync(itemId, completedOn, cancellationToken);
    }

    public async Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItem? item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.IncrementHabitPlusAsync(itemId, cancellationToken);
        }

        BoardItem? result = await _inner.IncrementHabitPlusAsync(itemId, cancellationToken);
        if (result is not null && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Increment + for \"{item.Title}\"", async () =>
            {
                BoardItem? current = await FindItemAsync(itemId, CancellationToken.None);
                if (current is not null)
                {
                    await _inner.UpdateHabitAsync(
                        itemId,
                        new UpdateHabitArgs(
                            current.Title,
                            current.Notes,
                            current.Tags,
                            current.TrackPlus,
                            current.TrackMinus,
                            current.ResetPeriod,
                            Math.Max(0, current.Counter - 1),
                            current.NegativeCounter,
                            current.ChecklistJson,
                            current.SortOrder),
                        CancellationToken.None);
                }
            });
        }
        return result;
    }

    public async Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItem? item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.IncrementHabitMinusAsync(itemId, cancellationToken);
        }

        BoardItem? result = await _inner.IncrementHabitMinusAsync(itemId, cancellationToken);
        if (result is not null && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Increment − for \"{item.Title}\"", async () =>
            {
                BoardItem? current = await FindItemAsync(itemId, CancellationToken.None);
                if (current is not null)
                {
                    await _inner.UpdateHabitAsync(
                        itemId,
                        new UpdateHabitArgs(
                            current.Title,
                            current.Notes,
                            current.Tags,
                            current.TrackPlus,
                            current.TrackMinus,
                            current.ResetPeriod,
                            current.Counter,
                            Math.Max(0, current.NegativeCounter - 1),
                            current.ChecklistJson,
                            current.SortOrder),
                        CancellationToken.None);
                }
            });
        }
        return result;
    }

    public async Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        UpdateHabitArgs args,
        CancellationToken cancellationToken = default)
    {
        BoardItem? item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.UpdateHabitAsync(itemId, args, cancellationToken);
        }

        BoardItem? result = await _inner.UpdateHabitAsync(itemId, args, cancellationToken);

        if (result is not null && !_undoService.IsUndoing
            && !IsHabitReorderOnly(item, args))
        {
            _undoService.RegisterUndo($"Edit \"{item.Title}\"", async () =>
            {
                await _inner.UpdateHabitAsync(
                    itemId,
                    new UpdateHabitArgs(
                        item.Title,
                        item.Notes,
                        item.Tags,
                        item.TrackPlus,
                        item.TrackMinus,
                        item.ResetPeriod,
                        item.Counter,
                        item.NegativeCounter,
                        item.ChecklistJson,
                        item.SortOrder),
                    CancellationToken.None);
            });
        }
        return result;
    }

    public async Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        UpdateTodoArgs args,
        CancellationToken cancellationToken = default)
    {
        BoardItem? item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.UpdateTodoAsync(itemId, args, cancellationToken);
        }

        BoardItem? result = await _inner.UpdateTodoAsync(itemId, args, cancellationToken);

        if (result is not null && !_undoService.IsUndoing
            && !IsTodoReorderOnly(item, args))
        {
            _undoService.RegisterUndo($"Edit \"{item.Title}\"", async () =>
            {
                await _inner.UpdateTodoAsync(
                    itemId,
                    new UpdateTodoArgs(
                        item.Title,
                        item.Notes,
                        item.Tags,
                        item.ChecklistJson,
                        item.TodoDueDate?.ToDateTime(TimeOnly.MinValue),
                        item.SortOrder),
                    CancellationToken.None);
            });
        }
        return result;
    }

    public async Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        UpdateDailyArgs args,
        CancellationToken cancellationToken = default)
    {
        BoardItem? item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.UpdateDailyAsync(itemId, args, cancellationToken);
        }

        BoardItem? result = await _inner.UpdateDailyAsync(itemId, args, cancellationToken);

        if (result is not null && !_undoService.IsUndoing
            && !IsDailyReorderOnly(item, args))
        {
            _undoService.RegisterUndo($"Edit \"{item.Title}\"", async () =>
            {
                await _inner.UpdateDailyAsync(
                    itemId,
                    new UpdateDailyArgs(
                        item.Title,
                        item.Notes,
                        item.Tags,
                        item.DailyStartDate?.ToDateTime(TimeOnly.MinValue),
                        item.DailyRepeat,
                        item.DailyRepeatInterval,
                        item.ChecklistJson,
                        item.Counter,
                        item.SortOrder),
                    CancellationToken.None);
            });
        }
        return result;
    }

    private static bool IsHabitReorderOnly(
        BoardItem item,
        UpdateHabitArgs args) =>
        args.SortOrder.HasValue
        && item.Title == args.Title
        && item.Notes == args.Notes
        && item.Tags == args.Tags
        && item.TrackPlus == args.TrackPlus
        && item.TrackMinus == args.TrackMinus
        && item.ResetPeriod == args.ResetPeriod
        && item.Counter == args.Counter
        && item.NegativeCounter == args.NegativeCounter
        && item.ChecklistJson == args.ChecklistJson;

    private static bool IsTodoReorderOnly(
        BoardItem item,
        UpdateTodoArgs args) =>
        args.SortOrder.HasValue
        && item.Title == args.Title
        && item.Notes == args.Notes
        && item.Tags == args.Tags
        && item.ChecklistJson == args.ChecklistJson
        && DatesEqual(item.TodoDueDate, args.DueDate);

    private static bool IsDailyReorderOnly(
        BoardItem item,
        UpdateDailyArgs args) =>
        args.SortOrder.HasValue
        && item.Title == args.Title
        && item.Notes == args.Notes
        && item.Tags == args.Tags
        && DatesEqual(item.DailyStartDate, args.StartDate)
        && item.DailyRepeat == args.RepeatType
        && item.DailyRepeatInterval == args.RepeatInterval
        && item.ChecklistJson == args.ChecklistJson
        && item.Counter == args.Streak;

    private static bool DatesEqual(DateOnly? itemDate, DateTime? dateTime)
    {
        if (itemDate is null && dateTime is null)
        {
            return true;
        }

        if (itemDate is null || dateTime is null)
        {
            return false;
        }

        return itemDate == DateOnly.FromDateTime(dateTime.Value);
    }

    private async Task<BoardItem?> FindItemAsync(Guid id, CancellationToken cancellationToken)
    {
        BoardSnapshot snap = await _inner.GetSnapshotAsync(cancellationToken);
        return snap.Habits.FirstOrDefault(x => x.Id == id)
            ?? snap.Dailies.FirstOrDefault(x => x.Id == id)
            ?? snap.Todos.FirstOrDefault(x => x.Id == id);
    }
}
