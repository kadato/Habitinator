using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public sealed class LocalBoardDataService : IBoardDataService
{
    private readonly List<BoardItem> _habits =
    [
        new(Guid.NewGuid(), "Drink a glass of water", false, 3),
        new(Guid.NewGuid(), "Read 10 pages", false, 1)
    ];

    private readonly List<BoardItem> _dailies =
    [
        new(Guid.NewGuid(), "Workout"),
        new(Guid.NewGuid(), "Deep work block"),
        new(Guid.NewGuid(), "Progress thesis")
    ];

    private readonly List<BoardItem> _todos =
    [
        new(Guid.NewGuid(), "Submit assignment"),
        new(Guid.NewGuid(), "Print report"),
        new(Guid.NewGuid(), "Call advisor")
    ];

    public Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BoardSnapshot(
            _habits.ToList(),
            _dailies.ToList(),
            _todos.ToList()));
    }

    public Task<BoardItem> CreateItemAsync(BoardSection section, string title, CancellationToken cancellationToken = default)
    {
        var item = new BoardItem(Guid.NewGuid(), title);
        GetSection(section).Add(item);
        return Task.FromResult(item);
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

        var updated = existing with { IsCompleted = !existing.IsCompleted };
        var index = list.IndexOf(existing);
        list[index] = updated;
        return Task.FromResult<BoardItem?>(updated);
    }

    public Task<BoardItem?> IncrementHabitAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var existing = _habits.FirstOrDefault(x => x.Id == itemId);
        if (existing is null)
        {
            return Task.FromResult<BoardItem?>(null);
        }

        var updated = existing with { Counter = existing.Counter + 1 };
        var index = _habits.IndexOf(existing);
        _habits[index] = updated;
        return Task.FromResult<BoardItem?>(updated);
    }

    private List<BoardItem> GetSection(BoardSection section) => section switch
    {
        BoardSection.Habit => _habits,
        BoardSection.Daily => _dailies,
        BoardSection.Todo => _todos,
        _ => throw new ArgumentOutOfRangeException(nameof(section))
    };
}
