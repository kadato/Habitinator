using System.ComponentModel.DataAnnotations;

namespace App.Shared.RCL.Models;

public sealed record ItemTitleRequest(
    [Required, StringLength(200)] string Title,
    Guid? ItemId = null);

public sealed record HabitUpdateRequest(
    [Required, StringLength(200)] string Title,
    [StringLength(4000)] string? Notes,
    [StringLength(500)] string? Tags,
    bool TrackPlus,
    bool TrackMinus,
    HabitResetPeriod ResetPeriod,
    int Counter,
    int NegativeCounter,
    [StringLength(8000)] string? ChecklistJson = null,
    double? SortOrder = null);

public sealed record DailyUpdateRequest(
    [Required, StringLength(200)] string Title,
    [StringLength(4000)] string? Notes,
    [StringLength(500)] string? Tags,
    DateOnly? StartDate,
    DailyRepeatType Repeat,
    int RepeatInterval,
    [StringLength(8000)] string? ChecklistJson,
    int Counter = 0,
    double? SortOrder = null);

public sealed record TodoUpdateRequest(
    [Required, StringLength(200)] string Title,
    [StringLength(4000)] string? Notes,
    [StringLength(500)] string? Tags,
    [StringLength(8000)] string? ChecklistJson,
    DateOnly? DueDate,
    double? SortOrder = null,
    int? TodoRepeatIntervalDays = null);

public sealed record DailyCompleteForDateRequest(DateOnly CompletedOn);

