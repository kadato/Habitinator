namespace App.Web.Data;

public enum ActivityEventType : byte
{
    HabitPlus = 1,
    HabitMinus = 2,
    DailyComplete = 3,
    DailyUncomplete = 4,
    TodoComplete = 5,
    TodoUncomplete = 6,
    TimerSession = 7
}
