using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public sealed class LocalBoardDataService : IBoardDataService
{
    private readonly List<BoardItem> _habits =
    [
        new(Guid.NewGuid(), "Drink a glass of water", false, 3, null, null, true, true, 2),
        new(Guid.NewGuid(), "Read 10 pages", false, 1)
    ];

    private readonly List<BoardItem> _dailies =
    [
        new(
            Guid.NewGuid(),
            "Workout",
            false,
            0,
            null,
            null,
            true,
            true,
            0,
            HabitResetPeriod.Daily,
            DailySchedule.UtcToday,
            DailyRepeatType.Daily,
            1,
            null,
            null,
            null),
        new(
            Guid.NewGuid(),
            "Deep work block",
            true,
            0,
            null,
            null,
            true,
            true,
            0,
            HabitResetPeriod.Daily,
            DailySchedule.UtcToday,
            DailyRepeatType.Daily,
            1,
            null,
            DailySchedule.UtcToday,
            null),
        new(
            Guid.NewGuid(),
            "Progress thesis",
            false,
            0,
            null,
            null,
            true,
            true,
            0,
            HabitResetPeriod.Daily,
            DailySchedule.UtcToday,
            DailyRepeatType.Daily,
            1,
            null,
            null,
            null)
    ];

    private readonly List<BoardItem> _todos =
    [
        new(Guid.NewGuid(), "Submit assignment"),
        new(Guid.NewGuid(), "Print report"),
        new(Guid.NewGuid(), "Call advisor")
    ];

    public Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        DateOnly today = DailySchedule.UtcToday;
        return Task.FromResult(new BoardSnapshot(
            _habits.ToList(),
            _dailies
                .Select(d => ProjectDailyForDisplay(d, today))
                .OrderBy(x => x.IsCompleted ? 1 : 0)
                .ThenBy(x => x.Title, StringComparer.Ordinal)
                .ThenBy(x => x.Id)
                .ToList(),
            _todos
                .OrderBy(x => x.IsCompleted)
                .ThenBy(x => x.Title, StringComparer.Ordinal)
                .ThenBy(x => x.Id)
                .ToList()));
    }

    private static BoardItem ProjectDailyForDisplay(BoardItem d, DateOnly today)
    {
        bool done = d.DailyLastCompletedOn == today
            || (d.DailyLastCompletedOn is null && d.IsCompleted);
        return d with { IsCompleted = done };
    }

    public Task<BoardItem> CreateItemAsync(BoardSection section, string title, CancellationToken cancellationToken = default)
    {
        var today = DailySchedule.UtcToday;
        var item = section == BoardSection.Daily
            ? new BoardItem(
                Guid.NewGuid(),
                title,
                false,
                0,
                null,
                null,
                true,
                true,
                0,
                HabitResetPeriod.Daily,
                today,
                DailyRepeatType.Daily,
                1,
                null,
                null,
                null)
            : new BoardItem(Guid.NewGuid(), title);
        GetSection(section).Add(item);
        return Task.FromResult(section == BoardSection.Daily ? ProjectDailyForDisplay(item, today) : item);
    }

    public Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title, CancellationToken cancellationToken = default)
    {
        var list = GetSection(section);
        var existing = list.FirstOrDefault(x => x.Id == itemId);
        if (existing is null)
        {
            return Task.FromResult<BoardItem?>(null);
        }

        var updated = existing with { Title = title };
        var index = list.IndexOf(existing);
        list[index] = updated;
        return Task.FromResult<BoardItem?>(updated);
    }

    public Task<bool> DeleteItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        var list = GetSection(section);
        return Task.FromResult(list.RemoveAll(x => x.Id == itemId) > 0);
    }

    public Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn, CancellationToken cancellationToken = default)
    {
        DateOnly today = DailySchedule.UtcToday;
        if (completedOn >= today)
        {
            return Task.FromResult<BoardItem?>(null);
        }

        var list = _dailies;
        var existing = list.FirstOrDefault(x => x.Id == itemId);
        if (existing is null)
        {
            return Task.FromResult<BoardItem?>(null);
        }

        if (existing.DailyLastCompletedOn == today)
        {
            return Task.FromResult<BoardItem?>(null);
        }

        if (!DailySchedule.IsDueOnDate(existing, completedOn))
        {
            return Task.FromResult<BoardItem?>(null);
        }

        var updated = existing with
        {
            DailyLastCompletedOn = completedOn,
            IsCompleted = true
        };
        int index = list.IndexOf(existing);
        list[index] = updated;
        return Task.FromResult<BoardItem?>(ProjectDailyForDisplay(updated, today));
    }

    public Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        if (section == BoardSection.Habit)
        {
            return Task.FromResult<BoardItem?>(null);
        }

        var list = GetSection(section);
        var existing = list.FirstOrDefault(x => x.Id == itemId);
        if (existing is null)
        {
            return Task.FromResult<BoardItem?>(null);
        }

        BoardItem updated;
        if (section == BoardSection.Daily)
        {
            DateOnly today = DailySchedule.UtcToday;
            if (ProjectDailyForDisplay(existing, today).IsCompleted)
            {
                updated = existing with { DailyLastCompletedOn = null, IsCompleted = false };
            }
            else
            {
                updated = existing with { DailyLastCompletedOn = today, IsCompleted = true };
            }
        }
        else
        {
            updated = existing with { IsCompleted = !existing.IsCompleted };
        }

        var index = list.IndexOf(existing);
        list[index] = updated;
        return Task.FromResult<BoardItem?>(section == BoardSection.Daily
            ? ProjectDailyForDisplay(updated, DailySchedule.UtcToday)
            : updated);
    }

    public Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var existing = _habits.FirstOrDefault(x => x.Id == itemId);
        if (existing is null || !existing.TrackPlus)
        {
            return Task.FromResult<BoardItem?>(null);
        }

        var updated = existing with { Counter = existing.Counter + 1 };
        var index = _habits.IndexOf(existing);
        _habits[index] = updated;
        return Task.FromResult<BoardItem?>(updated);
    }

    public Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var existing = _habits.FirstOrDefault(x => x.Id == itemId);
        if (existing is null || !existing.TrackMinus)
        {
            return Task.FromResult<BoardItem?>(null);
        }

        var updated = existing with { NegativeCounter = existing.NegativeCounter + 1 };
        var index = _habits.IndexOf(existing);
        _habits[index] = updated;
        return Task.FromResult<BoardItem?>(updated);
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
        CancellationToken cancellationToken = default)
    {
        var existing = _habits.FirstOrDefault(x => x.Id == itemId);
        if (existing is null)
        {
            return Task.FromResult<BoardItem?>(null);
        }

        if (!trackPlus && !trackMinus)
        {
            trackPlus = true;
            trackMinus = true;
        }

        var updated = existing with
        {
            Title = title,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim(),
            TrackPlus = trackPlus,
            TrackMinus = trackMinus,
            ResetPeriod = resetPeriod,
            Counter = Math.Max(0, counter),
            NegativeCounter = Math.Max(0, negativeCounter),
            ChecklistJson = string.IsNullOrWhiteSpace(checklistJson) ? null : checklistJson.Trim()
        };
        var index = _habits.IndexOf(existing);
        _habits[index] = updated;
        return Task.FromResult<BoardItem?>(updated);
    }

    public Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        string? checklistJson,
        DateTime? dueDate,
        CancellationToken cancellationToken = default)
    {
        var existing = _todos.FirstOrDefault(x => x.Id == itemId);
        if (existing is null)
        {
            return Task.FromResult<BoardItem?>(null);
        }

        DateOnly? due = dueDate is { } d ? DateOnly.FromDateTime(d) : null;
        var updated = existing with
        {
            Title = title,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim(),
            ChecklistJson = string.IsNullOrWhiteSpace(checklistJson) ? null : checklistJson.Trim(),
            TodoDueDate = due
        };
        int index = _todos.IndexOf(existing);
        _todos[index] = updated;
        return Task.FromResult<BoardItem?>(updated);
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
        CancellationToken cancellationToken = default)
    {
        var existing = _dailies.FirstOrDefault(x => x.Id == itemId);
        if (existing is null)
        {
            return Task.FromResult<BoardItem?>(null);
        }

        int n = Math.Max(1, Math.Min(999, repeatInterval));
        int streakClamped = Math.Max(0, Math.Min(9999, streak));
        var updated = existing with
        {
            Title = title,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim(),
            DailyStartDate = startDate is { } d ? DateOnly.FromDateTime(d) : null,
            DailyRepeat = repeatType,
            DailyRepeatInterval = n,
            ChecklistJson = string.IsNullOrWhiteSpace(checklistJson) ? null : checklistJson.Trim(),
            Counter = streakClamped
        };
        int index = _dailies.IndexOf(existing);
        _dailies[index] = updated;
        return Task.FromResult<BoardItem?>(ProjectDailyForDisplay(updated, DailySchedule.UtcToday));
    }

    private List<BoardItem> GetSection(BoardSection section) => section switch
    {
        BoardSection.Habit => _habits,
        BoardSection.Daily => _dailies,
        BoardSection.Todo => _todos,
        _ => throw new ArgumentOutOfRangeException(nameof(section))
    };
}
