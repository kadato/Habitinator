namespace App.Shared.RCL.Models;

public enum BoardOutboxOperationKind
{
    Create = 0,
    Rename = 1,
    Delete = 2,
    Toggle = 3,
    CompleteDailyForDate = 4,
    HabitIncrement = 5,
    HabitDecrement = 6,
    UpdateHabit = 7,
    UpdateTodo = 8,
    UpdateDaily = 9,
    Archive = 10,
    Unarchive = 11
}
