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

    public async Task<BoardItem> CreateItemAsync(BoardSection section, string title, CancellationToken cancellationToken = default)
    {
        var item = await _inner.CreateItemAsync(section, title, cancellationToken);
        if (!_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Add \"{title}\"", async () =>
            {
                await _inner.DeleteItemAsync(section, item.Id, CancellationToken.None);
            });
        }
        return item;
    }

    public async Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title, CancellationToken cancellationToken = default)
    {
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null) return await _inner.RenameItemAsync(section, itemId, title, cancellationToken);

        var oldTitle = item.Title;
        var result = await _inner.RenameItemAsync(section, itemId, title, cancellationToken);
        if (result is not null && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Rename \"{oldTitle}\" to \"{title}\"", async () =>
            {
                await _inner.RenameItemAsync(section, itemId, oldTitle, CancellationToken.None);
            });
        }
        return result;
    }

    public async Task<bool> DeleteItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null) return await _inner.DeleteItemAsync(section, itemId, cancellationToken);

        var success = await _inner.DeleteItemAsync(section, itemId, cancellationToken);
        if (success && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Delete \"{item.Title}\"", async () =>
            {
                var recreated = await _inner.CreateItemAsync(section, item.Title, CancellationToken.None);
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
        if (item is null) return await _inner.IncrementHabitPlusAsync(itemId, cancellationToken);

        var result = await _inner.IncrementHabitPlusAsync(itemId, cancellationToken);
        if (result is not null && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Increment + for \"{item.Title}\"", async () =>
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
                    CancellationToken.None);
            });
        }
        return result;
    }

    public async Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null) return await _inner.IncrementHabitMinusAsync(itemId, cancellationToken);

        var result = await _inner.IncrementHabitMinusAsync(itemId, cancellationToken);
        if (result is not null && !_undoService.IsUndoing)
        {
            _undoService.RegisterUndo($"Increment − for \"{item.Title}\"", async () =>
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
                    CancellationToken.None);
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
        CancellationToken cancellationToken = default)
    {
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.UpdateHabitAsync(
                itemId, title, notes, tags, trackPlus, trackMinus, resetPeriod,
                counter, negativeCounter, checklistJson, cancellationToken);
        }

        var result = await _inner.UpdateHabitAsync(
            itemId, title, notes, tags, trackPlus, trackMinus, resetPeriod,
            counter, negativeCounter, checklistJson, cancellationToken);

        if (result is not null && !_undoService.IsUndoing)
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
        CancellationToken cancellationToken = default)
    {
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.UpdateTodoAsync(itemId, title, notes, tags, checklistJson, dueDate, cancellationToken);
        }

        var result = await _inner.UpdateTodoAsync(itemId, title, notes, tags, checklistJson, dueDate, cancellationToken);

        if (result is not null && !_undoService.IsUndoing)
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
        CancellationToken cancellationToken = default)
    {
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.UpdateDailyAsync(
                itemId, title, notes, tags, startDate, repeatType, repeatInterval, checklistJson, streak, cancellationToken);
        }

        var result = await _inner.UpdateDailyAsync(
            itemId, title, notes, tags, startDate, repeatType, repeatInterval, checklistJson, streak, cancellationToken);

        if (result is not null && !_undoService.IsUndoing)
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
                    CancellationToken.None);
            });
        }
        return result;
    }

    private async Task<BoardItem?> FindItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var snap = await _inner.GetSnapshotAsync(cancellationToken);
        return snap.Habits.FirstOrDefault(x => x.Id == id)
            ?? snap.Dailies.FirstOrDefault(x => x.Id == id)
            ?? snap.Todos.FirstOrDefault(x => x.Id == id);
    }
}
