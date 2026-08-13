namespace App.Shared.RCL.Models;

public enum EditDialogAction
{
    Close,
    Archive,
    Delete
}

public record EditDailyDialogResult(
    EditDialogAction Action,
    string Title,
    string? Notes,
    string? Tags,
    DateOnly StartDate,
    DailyRepeatType Repeat,
    int RepeatInterval,
    string? ChecklistJson,
    int Counter
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
    DateOnly? DueDate,
    int? RepeatIntervalDays = null
);
