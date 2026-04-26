namespace App.Shared.RCL.Models;

public sealed record ItemTitleRequest(string Title);

public sealed record HabitUpdateRequest(
    string Title,
    string? Notes,
    string? Tags,
    bool TrackPlus,
    bool TrackMinus,
    HabitResetPeriod ResetPeriod,
    int Counter,
    int NegativeCounter,
    string? ChecklistJson = null);

public sealed record DailyUpdateRequest(
    string Title,
    string? Notes,
    string? Tags,
    DateTime? StartDate,
    DailyRepeatType Repeat,
    int RepeatInterval,
    string? ChecklistJson,
    int Streak = 0);

public sealed record TodoUpdateRequest(
    string Title,
    string? Notes,
    string? Tags,
    string? ChecklistJson,
    DateTime? DueDate);

public sealed record BoardSectionRequest(BoardSection Section);

public sealed record DailyCompleteForDateRequest(DateOnly CompletedOn);
