namespace App.Shared.RCL.Models;

public enum BoardSection
{
    Habit,
    Daily,
    Todo
}

public sealed record BoardItem(Guid Id, string Title, bool IsCompleted = false, int Counter = 0);

public sealed record BoardSnapshot(
    IReadOnlyList<BoardItem> Habits,
    IReadOnlyList<BoardItem> Dailies,
    IReadOnlyList<BoardItem> Todos);
