namespace App.Shared.RCL.Models;

public enum EditDialogAction
{
    Save,
    Archive,
    Delete
}

public record EditDailyDialogResult(
    EditDialogAction Action,
    string Title,
    string? Notes,
    string? Tags,
    DateTime StartDate,
    DailyRepeatType Repeat,
    int RepeatInterval,
    string? ChecklistJson,
    int Streak
);

public record EditHabitDialogResult(
    EditDialogAction Action,
    string Title,
    string? Notes,
    string? Tags,
    bool TrackPlus,
    bool TrackMinus,
    HabitResetPeriod ResetPeriod,
    int Counter,
    int NegativeCounter,
    string? ChecklistJson
);

public record EditTodoDialogResult(
    EditDialogAction Action,
    string Title,
    string? Notes,
    string? Tags,
    string? ChecklistJson,
    DateTime? DueDate,
    int? RepeatIntervalDays = null
);
