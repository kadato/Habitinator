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

    public Task<BoardItem?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        return _inner.GetItemAsync(itemId, cancellationToken);
    }

    public Task<Dictionary<Guid, int>> GetStreakMapAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetStreakMapAsync(cancellationToken);
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
            }, [ItemKey(itemId)]);
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
            }, [ItemKey(itemId)]);
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
            }, [ItemKey(item.Id)]);
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
            }, [$"item:{itemId:N}:title"]);
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
                await RestoreDeletedItemAsync(section, item).ConfigureAwait(false);
            }, [ItemKey(item.Id)]);
        }
        return success;
    }

    private async Task RestoreDeletedItemAsync(BoardSection section, BoardItem item)
    {
        var recreated = await _inner.CreateItemAsync(section, item.Title, item.Id, CancellationToken.None).ConfigureAwait(false);
        if (section == BoardSection.Habit)
        {
            await _inner.UpdateHabitAsync(
                recreated.Id,
                UpdateHabitArgs.From(item),
                CancellationToken.None).ConfigureAwait(false);
        }
        else if (section == BoardSection.Daily)
        {
            await _inner.UpdateDailyAsync(
                recreated.Id,
                UpdateDailyArgs.From(item),
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
                UpdateTodoArgs.From(item),
                CancellationToken.None).ConfigureAwait(false);
            if (item.IsCompleted)
            {
                await _inner.ToggleItemAsync(BoardSection.Todo, recreated.Id, CancellationToken.None).ConfigureAwait(false);
            }
        }
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
            }, [$"item:{itemId:N}:completed"]);
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
            _undoService.RegisterUndo($"Incremented \"{item.Title}\"", async () =>
            {
                var current = await FindItemAsync(itemId, CancellationToken.None);
                if (current is not null)
                {
                    await _inner.UpdateHabitAsync(
                        itemId,
                        UpdateHabitArgs.From(current) with { Counter = Math.Max(0, current.Counter - 1) },
                        CancellationToken.None);
                }
            }, [$"item:{itemId:N}:counter"]);
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
            _undoService.RegisterUndo($"Decremented \"{item.Title}\"", async () =>
            {
                var current = await FindItemAsync(itemId, CancellationToken.None);
                if (current is not null)
                {
                    await _inner.UpdateHabitAsync(
                        itemId,
                        UpdateHabitArgs.From(current) with { NegativeCounter = Math.Max(0, current.NegativeCounter - 1) },
                        CancellationToken.None);
                }
            }, [$"item:{itemId:N}:counter"]);
        }
        return result;
    }

    public async Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        UpdateHabitArgs args,
        CancellationToken cancellationToken = default)
    {
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.UpdateHabitAsync(itemId, args, cancellationToken);
        }

        var result = await _inner.UpdateHabitAsync(itemId, args, cancellationToken);

        var diff = DiffHabit(item, args);
        if (result is not null && !_undoService.IsUndoing && diff.Count > 0)
        {
            _undoService.RegisterUndo(
                $"Edit \"{item.Title}\"",
                () => UndoHabitEditAsync(itemId, diff),
                KeysFor(itemId, diff));
        }
        return result;
    }

    public async Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        UpdateTodoArgs args,
        CancellationToken cancellationToken = default)
    {
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.UpdateTodoAsync(itemId, args, cancellationToken);
        }

        var result = await _inner.UpdateTodoAsync(itemId, args, cancellationToken);

        var diff = DiffTodo(item, args);
        if (result is not null && !_undoService.IsUndoing && diff.Count > 0)
        {
            _undoService.RegisterUndo(
                $"Edit \"{item.Title}\"",
                () => UndoTodoEditAsync(itemId, diff),
                KeysFor(itemId, diff));
        }
        return result;
    }

    public async Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        UpdateDailyArgs args,
        CancellationToken cancellationToken = default)
    {
        var item = await FindItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return await _inner.UpdateDailyAsync(itemId, args, cancellationToken);
        }

        var result = await _inner.UpdateDailyAsync(itemId, args, cancellationToken);

        var diff = DiffDaily(item, args);
        if (result is not null && !_undoService.IsUndoing && diff.Count > 0)
        {
            _undoService.RegisterUndo(
                $"Edit \"{item.Title}\"",
                () => UndoDailyEditAsync(itemId, diff),
                KeysFor(itemId, diff));
        }
        return result;
    }

    private const string FieldTitle = "title";
    private const string FieldNotes = "notes";
    private const string FieldTags = "tags";
    private const string FieldCounter = "counter";
    private const string FieldNegativeCounter = "negcounter";
    private const string ChecklistFieldPrefix = "checklist:";

    private static string ItemKey(Guid itemId) => $"item:{itemId:N}";

    private static T DiffCast<T>(object? value, T fallback) => value is T typed ? typed : fallback;

    private static List<string> KeysFor(Guid itemId, Dictionary<string, object?> diff)
    {
        var keys = new List<string>(diff.Count);
        foreach (var field in diff.Keys)
        {
            keys.Add($"{ItemKey(itemId)}:{field}");
        }
        return keys;
    }

    private static Dictionary<string, object?> DiffHabit(BoardItem item, UpdateHabitArgs args)
    {
        var diff = new Dictionary<string, object?>();
        if (!string.Equals(item.Title, args.Title, StringComparison.Ordinal))
        {
            diff[FieldTitle] = item.Title;
        }
        if (!string.Equals(item.Notes, args.Notes, StringComparison.Ordinal))
        {
            diff[FieldNotes] = item.Notes;
        }
        if (!string.Equals(item.Tags, args.Tags, StringComparison.Ordinal))
        {
            diff[FieldTags] = item.Tags;
        }
        if (item.TrackPlus != args.TrackPlus)
        {
            diff["trackplus"] = item.TrackPlus;
        }
        if (item.TrackMinus != args.TrackMinus)
        {
            diff["trackminus"] = item.TrackMinus;
        }
        if (item.ResetPeriod != args.ResetPeriod)
        {
            diff["resetperiod"] = item.ResetPeriod;
        }
        if (item.Counter != args.Counter)
        {
            diff[FieldCounter] = item.Counter;
        }
        if (item.NegativeCounter != args.NegativeCounter)
        {
            diff[FieldNegativeCounter] = item.NegativeCounter;
        }
        DiffChecklist(diff, item.ChecklistJson, args.ChecklistJson);
        return diff;
    }

    private static Dictionary<string, object?> DiffTodo(BoardItem item, UpdateTodoArgs args)
    {
        var diff = new Dictionary<string, object?>();
        if (!string.Equals(item.Title, args.Title, StringComparison.Ordinal))
        {
            diff[FieldTitle] = item.Title;
        }
        if (!string.Equals(item.Notes, args.Notes, StringComparison.Ordinal))
        {
            diff[FieldNotes] = item.Notes;
        }
        if (!string.Equals(item.Tags, args.Tags, StringComparison.Ordinal))
        {
            diff[FieldTags] = item.Tags;
        }
        if (!DatesEqual(item.TodoDueDate, args.DueDate))
        {
            diff["duedate"] = item.TodoDueDate;
        }
        if (item.TodoRepeatIntervalDays != args.TodoRepeatIntervalDays)
        {
            diff["repeatdays"] = item.TodoRepeatIntervalDays;
        }
        DiffChecklist(diff, item.ChecklistJson, args.ChecklistJson);
        return diff;
    }

    private static Dictionary<string, object?> DiffDaily(BoardItem item, UpdateDailyArgs args)
    {
        var diff = new Dictionary<string, object?>();
        if (!string.Equals(item.Title, args.Title, StringComparison.Ordinal))
        {
            diff[FieldTitle] = item.Title;
        }
        if (!string.Equals(item.Notes, args.Notes, StringComparison.Ordinal))
        {
            diff[FieldNotes] = item.Notes;
        }
        if (!string.Equals(item.Tags, args.Tags, StringComparison.Ordinal))
        {
            diff[FieldTags] = item.Tags;
        }
        if (!DatesEqual(item.DailyStartDate, args.StartDate))
        {
            diff["startdate"] = item.DailyStartDate;
        }
        if (item.DailyRepeat != args.Repeat)
        {
            diff["repeat"] = item.DailyRepeat;
        }
        if (item.DailyRepeatInterval != args.RepeatInterval)
        {
            diff["interval"] = item.DailyRepeatInterval;
        }
        if (item.Counter != args.Counter)
        {
            diff[FieldCounter] = item.Counter;
        }
        DiffChecklist(diff, item.ChecklistJson, args.ChecklistJson);
        return diff;
    }

    /// <summary>
    ///     Records only the checklist lines whose done state changed. When the line ids differ,
    ///     lines added or removed, the whole field is treated as one change.
    /// </summary>
    private static void DiffChecklist(Dictionary<string, object?> diff, string? beforeJson, string? afterJson)
    {
        if (string.Equals(beforeJson ?? string.Empty, afterJson ?? string.Empty, StringComparison.Ordinal))
        {
            return;
        }

        var before = DailyChecklistJson.Parse(beforeJson);
        var after = DailyChecklistJson.Parse(afterJson);

        if (before.Count != after.Count || before.Any(b => !after.Any(a => a.Id == b.Id)))
        {
            diff["checklist"] = beforeJson;
            return;
        }

        foreach (var beforeLine in before)
        {
            var afterLine = after.First(a => a.Id == beforeLine.Id);
            if (afterLine.IsDone != beforeLine.IsDone)
            {
                diff[$"{ChecklistFieldPrefix}{beforeLine.Id:N}"] = beforeLine.IsDone;
            }
        }
    }

    private static string? ApplyChecklistDiff(string? currentJson, Dictionary<string, object?> diff)
    {
        if (diff.TryGetValue("checklist", out var whole))
        {
            return (string?)whole;
        }

        var lineKeys = diff.Keys.Where(k => k.StartsWith(ChecklistFieldPrefix, StringComparison.Ordinal)).ToList();
        if (lineKeys.Count == 0)
        {
            return currentJson;
        }

        var rows = DailyChecklistJson.Parse(currentJson).ToList();
        foreach (var key in lineKeys)
        {
            var lineId = Guid.Parse(key.AsSpan(ChecklistFieldPrefix.Length));
            var i = rows.FindIndex(x => x.Id == lineId);
            if (i < 0)
            {
                return currentJson;
            }

            rows[i] = rows[i] with { IsDone = DiffCast(diff[key], rows[i].IsDone) };
        }

        return DailyChecklistJson.Serialize(rows);
    }

    private async Task UndoHabitEditAsync(Guid itemId, Dictionary<string, object?> diff)
    {
        var current = await FindItemAsync(itemId, CancellationToken.None);
        if (current is null)
        {
            return;
        }

        await _inner.UpdateHabitAsync(itemId, UpdateHabitArgs.From(current) with
        {
            Title = diff.TryGetValue(FieldTitle, out var title) ? DiffCast(title, current.Title) : current.Title,
            Notes = diff.TryGetValue(FieldNotes, out var notes) ? (string?)notes : current.Notes,
            Tags = diff.TryGetValue(FieldTags, out var tags) ? (string?)tags : current.Tags,
            TrackPlus = diff.TryGetValue("trackplus", out var trackPlus) ? DiffCast(trackPlus, current.TrackPlus) : current.TrackPlus,
            TrackMinus = diff.TryGetValue("trackminus", out var trackMinus) ? DiffCast(trackMinus, current.TrackMinus) : current.TrackMinus,
            ResetPeriod = diff.TryGetValue("resetperiod", out var resetPeriod) ? DiffCast(resetPeriod, current.ResetPeriod) : current.ResetPeriod,
            Counter = diff.TryGetValue(FieldCounter, out var counter) ? DiffCast(counter, current.Counter) : current.Counter,
            NegativeCounter = diff.TryGetValue(FieldNegativeCounter, out var negativeCounter) ? DiffCast(negativeCounter, current.NegativeCounter) : current.NegativeCounter,
            ChecklistJson = ApplyChecklistDiff(current.ChecklistJson, diff)
        }, CancellationToken.None);
    }

    private async Task UndoTodoEditAsync(Guid itemId, Dictionary<string, object?> diff)
    {
        var current = await FindItemAsync(itemId, CancellationToken.None);
        if (current is null)
        {
            return;
        }

        await _inner.UpdateTodoAsync(itemId, UpdateTodoArgs.From(current) with
        {
            Title = diff.TryGetValue(FieldTitle, out var title) ? DiffCast(title, current.Title) : current.Title,
            Notes = diff.TryGetValue(FieldNotes, out var notes) ? (string?)notes : current.Notes,
            Tags = diff.TryGetValue(FieldTags, out var tags) ? (string?)tags : current.Tags,
            ChecklistJson = ApplyChecklistDiff(current.ChecklistJson, diff),
            DueDate = diff.TryGetValue("duedate", out var dueDate) ? (DateOnly?)dueDate : current.TodoDueDate,
            TodoRepeatIntervalDays = diff.TryGetValue("repeatdays", out var repeatDays) ? (int?)repeatDays : current.TodoRepeatIntervalDays
        }, CancellationToken.None);
    }

    private async Task UndoDailyEditAsync(Guid itemId, Dictionary<string, object?> diff)
    {
        var current = await FindItemAsync(itemId, CancellationToken.None);
        if (current is null)
        {
            return;
        }

        await _inner.UpdateDailyAsync(itemId, UpdateDailyArgs.From(current) with
        {
            Title = diff.TryGetValue(FieldTitle, out var title) ? DiffCast(title, current.Title) : current.Title,
            Notes = diff.TryGetValue(FieldNotes, out var notes) ? (string?)notes : current.Notes,
            Tags = diff.TryGetValue(FieldTags, out var tags) ? (string?)tags : current.Tags,
            StartDate = diff.TryGetValue("startdate", out var startDate) ? (DateOnly?)startDate : current.DailyStartDate,
            Repeat = diff.TryGetValue("repeat", out var repeat) ? DiffCast(repeat, current.DailyRepeat) : current.DailyRepeat,
            RepeatInterval = diff.TryGetValue("interval", out var interval) ? DiffCast(interval, current.DailyRepeatInterval) : current.DailyRepeatInterval,
            ChecklistJson = ApplyChecklistDiff(current.ChecklistJson, diff),
            Counter = diff.TryGetValue(FieldCounter, out var counter) ? DiffCast(counter, current.Counter) : current.Counter
        }, CancellationToken.None);
    }

    private static bool DatesEqual(DateOnly? itemDate, DateOnly? otherDate) =>
        itemDate == otherDate;

    private async Task<BoardItem?> FindItemAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _inner.GetItemAsync(id, cancellationToken);
    }
}
