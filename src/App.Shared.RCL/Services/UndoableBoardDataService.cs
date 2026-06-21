using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public sealed class UndoableBoardDataService : IBoardDataService
{
    private readonly IBoardDataService _inner;
    private readonly IUndoService _undoService;

    public UndoableBoardDataService(IBoardDataService inner, IUndoService undoService)
    {
        _inner = inner;
        _undoService = undoService;
    }

    public Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetSnapshotAsync(cancellationToken);
    }

    public async Task<BoardItem?> ArchiveItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.ArchiveItemAsync(section, itemId, cancellationToken);
        }

        var result = await _inner.ArchiveItemAsync(section, itemId, cancellationToken);
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
        var result = await _inner.UnarchiveItemAsync(section, itemId, cancellationToken);
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
        var item = await _inner.CreateItemAsync(section, title, itemId, cancellationToken);
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
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.RenameItemAsync(section, itemId, title, cancellationToken);
        }

        var oldTitle = item.Title;
        var result = await _inner.RenameItemAsync(section, itemId, title, cancellationToken);
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
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.DeleteItemAsync(section, itemId, cancellationToken);
        }

        var success = await _inner.DeleteItemAsync(section, itemId, cancellationToken);
        if (success && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Delete \"{item.Title}\"", async () =>
            {
                var recreated = await _inner.CreateItemAsync(section, item.Title, item.Id, CancellationToken.None);
                if (section == BoardSection.Habit)
                {
                    await _inner.UpdateHabitAsync(
                        recreated.Id,
                        item.Title,
                        item.Notes,
                        item.Tags,
                        item.TrackPlus,
                        item.TrackMinus,
                        item.ResetPeriod,
                        item.Counter,
                        item.NegativeCounter,
                        item.ChecklistJson,
                        item.SortOrder,
                        CancellationToken.None);
                }
                else if (section == BoardSection.Daily)
                {
                    await _inner.UpdateDailyAsync(
                        recreated.Id,
                        item.Title,
                        item.Notes,
                        item.Tags,
                        item.DailyStartDate?.ToDateTime(TimeOnly.MinValue),
                        item.DailyRepeat,
                        item.DailyRepeatInterval,
                        item.ChecklistJson,
                        item.Counter,
                        item.SortOrder,
                        CancellationToken.None);
                    if (item.IsCompleted)
                    {
                        await _inner.ToggleItemAsync(BoardSection.Daily, recreated.Id, CancellationToken.None);
                    }
                }
                else if (section == BoardSection.Todo)
                {
                    await _inner.UpdateTodoAsync(
                        recreated.Id,
                        item.Title,
                        item.Notes,
                        item.Tags,
                        item.ChecklistJson,
                        item.TodoDueDate?.ToDateTime(TimeOnly.MinValue),
                        item.SortOrder,
                        CancellationToken.None);
                    if (item.IsCompleted)
                    {
                        await _inner.ToggleItemAsync(BoardSection.Todo, recreated.Id, CancellationToken.None);
                    }
                }
            });
        }
        return success;
    }

    public async Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        var result = await _inner.ToggleItemAsync(section, itemId, cancellationToken);
        if (result is not null && !_undoService.IsUndoing)
        {
            var actionVerb = result.IsCompleted ? "Complete" : "Uncomplete";
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
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.IncrementHabitPlusAsync(itemId, cancellationToken);
        }

        var result = await _inner.IncrementHabitPlusAsync(itemId, cancellationToken);
        if (result is not null && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Increment + for \"{item.Title}\"", async () =>
            {
                var current = await FindItemAsync(itemId, CancellationToken.None);
                if (current is not null)
                {
                    await _inner.UpdateHabitAsync(
                        itemId,
                        current.Title,
                        current.Notes,
                        current.Tags,
                        current.TrackPlus,
                        current.TrackMinus,
                        current.ResetPeriod,
                        Math.Max(0, current.Counter - 1),
                        current.NegativeCounter,
                        current.ChecklistJson,
                        current.SortOrder,
                        CancellationToken.None);
                }
            });
        }
        return result;
    }

    public async Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.IncrementHabitMinusAsync(itemId, cancellationToken);
        }

        var result = await _inner.IncrementHabitMinusAsync(itemId, cancellationToken);
        if (result is not null && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Increment − for \"{item.Title}\"", async () =>
            {
                var current = await FindItemAsync(itemId, CancellationToken.None);
                if (current is not null)
                {
                    await _inner.UpdateHabitAsync(
                        itemId,
                        current.Title,
                        current.Notes,
                        current.Tags,
                        current.TrackPlus,
                        current.TrackMinus,
                        current.ResetPeriod,
                        current.Counter,
                        Math.Max(0, current.NegativeCounter - 1),
                        current.ChecklistJson,
                        current.SortOrder,
                        CancellationToken.None);
                }
            });
        }
        return result;
    }

    public async Task<BoardItem?> UpdateHabitAsync(
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
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.UpdateHabitAsync(
                itemId, title, notes, tags, trackPlus, trackMinus, resetPeriod,
                counter, negativeCounter, checklistJson, sortOrder, cancellationToken);
        }

        var result = await _inner.UpdateHabitAsync(
            itemId, title, notes, tags, trackPlus, trackMinus, resetPeriod,
            counter, negativeCounter, checklistJson, sortOrder, cancellationToken);

        if (result is not null && !_undoService.IsUndoing
            && !IsHabitReorderOnly(item, title, notes, tags, trackPlus, trackMinus, resetPeriod, counter, negativeCounter, checklistJson, sortOrder))
        {
            _undoService.RegisterUndo($"Edit \"{item.Title}\"", async () =>
            {
                await _inner.UpdateHabitAsync(
                    itemId,
                    item.Title,
                    item.Notes,
                    item.Tags,
                    item.TrackPlus,
                    item.TrackMinus,
                    item.ResetPeriod,
                    item.Counter,
                    item.NegativeCounter,
                    item.ChecklistJson,
                    item.SortOrder,
                    CancellationToken.None);
            });
        }
        return result;
    }

    public async Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        string? checklistJson,
        DateTime? dueDate,
        double? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.UpdateTodoAsync(itemId, title, notes, tags, checklistJson, dueDate, sortOrder, cancellationToken);
        }

        var result = await _inner.UpdateTodoAsync(itemId, title, notes, tags, checklistJson, dueDate, sortOrder, cancellationToken);

        if (result is not null && !_undoService.IsUndoing
            && !IsTodoReorderOnly(item, title, notes, tags, checklistJson, dueDate, sortOrder))
        {
            _undoService.RegisterUndo($"Edit \"{item.Title}\"", async () =>
            {
                await _inner.UpdateTodoAsync(
                    itemId,
                    item.Title,
                    item.Notes,
                    item.Tags,
                    item.ChecklistJson,
                    item.TodoDueDate?.ToDateTime(TimeOnly.MinValue),
                    item.SortOrder,
                    CancellationToken.None);
            });
        }
        return result;
    }

    public async Task<BoardItem?> UpdateDailyAsync(
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
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.UpdateDailyAsync(
                itemId, title, notes, tags, startDate, repeatType, repeatInterval, checklistJson, streak, sortOrder, cancellationToken);
        }

        var result = await _inner.UpdateDailyAsync(
            itemId, title, notes, tags, startDate, repeatType, repeatInterval, checklistJson, streak, sortOrder, cancellationToken);

        if (result is not null && !_undoService.IsUndoing
            && !IsDailyReorderOnly(item, title, notes, tags, startDate, repeatType, repeatInterval, checklistJson, streak, sortOrder))
        {
            _undoService.RegisterUndo($"Edit \"{item.Title}\"", async () =>
            {
                await _inner.UpdateDailyAsync(
                    itemId,
                    item.Title,
                    item.Notes,
                    item.Tags,
                    item.DailyStartDate?.ToDateTime(TimeOnly.MinValue),
                    item.DailyRepeat,
                    item.DailyRepeatInterval,
                    item.ChecklistJson,
                    item.Counter,
                    item.SortOrder,
                    CancellationToken.None);
            });
        }
        return result;
    }

    private static bool IsHabitReorderOnly(
        BoardItem item,
        string title,
        string? notes,
        string? tags,
        bool trackPlus,
        bool trackMinus,
        HabitResetPeriod resetPeriod,
        int counter,
        int negativeCounter,
        string? checklistJson,
        double? sortOrder) =>
        sortOrder.HasValue
        && item.Title == title
        && item.Notes == notes
        && item.Tags == tags
        && item.TrackPlus == trackPlus
        && item.TrackMinus == trackMinus
        && item.ResetPeriod == resetPeriod
        && item.Counter == counter
        && item.NegativeCounter == negativeCounter
        && item.ChecklistJson == checklistJson;

    private static bool IsTodoReorderOnly(
        BoardItem item,
        string title,
        string? notes,
        string? tags,
        string? checklistJson,
        DateTime? dueDate,
        double? sortOrder) =>
        sortOrder.HasValue
        && item.Title == title
        && item.Notes == notes
        && item.Tags == tags
        && item.ChecklistJson == checklistJson
        && DatesEqual(item.TodoDueDate, dueDate);

    private static bool IsDailyReorderOnly(
        BoardItem item,
        string title,
        string? notes,
        string? tags,
        DateTime? startDate,
        DailyRepeatType repeatType,
        int repeatInterval,
        string? checklistJson,
        int streak,
        double? sortOrder) =>
        sortOrder.HasValue
        && item.Title == title
        && item.Notes == notes
        && item.Tags == tags
        && DatesEqual(item.DailyStartDate, startDate)
        && item.DailyRepeat == repeatType
        && item.DailyRepeatInterval == repeatInterval
        && item.ChecklistJson == checklistJson
        && item.Counter == streak;

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
        var snap = await _inner.GetSnapshotAsync(cancellationToken);
        return snap.Habits.FirstOrDefault(x => x.Id == id)
            ?? snap.Dailies.FirstOrDefault(x => x.Id == id)
            ?? snap.Todos.FirstOrDefault(x => x.Id == id);
    }
}
