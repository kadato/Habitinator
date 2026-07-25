namespace App.Shared.RCL.Models;

public enum ActivityEventType
{
    None = 0,
    HabitPlus = 1,
    HabitMinus = 2,
    DailyComplete = 3,
    DailyUncomplete = 4,
    TodoComplete = 5,
    TodoUncomplete = 6,
    TimerSession = 7
}
