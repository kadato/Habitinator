using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public sealed record UpdateHabitArgs(
    string Title,
    string? Notes,
    string? Tags,
    bool TrackPlus,
    bool TrackMinus,
    HabitResetPeriod ResetPeriod,
    int Counter,
    int NegativeCounter,
    string? ChecklistJson = null,
    double? SortOrder = null,
    DateTimeOffset? ExpectedUpdatedAtUtc = null);

public sealed record UpdateTodoArgs(
    string Title,
    string? Notes,
    string? Tags,
    string? ChecklistJson,
    DateTime? DueDate,
    double? SortOrder = null,
    int? TodoRepeatIntervalDays = null,
    DateTimeOffset? ExpectedUpdatedAtUtc = null);

public sealed record UpdateDailyArgs(
    string Title,
    string? Notes,
    string? Tags,
    DateTime? StartDate,
    DailyRepeatType RepeatType,
    int RepeatInterval,
    string? ChecklistJson,
    int Streak,
    double? SortOrder = null,
    DateTimeOffset? ExpectedUpdatedAtUtc = null);

public interface IBoardDataService
{
    Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches a single active board item by id, or null when not found.</summary>
    Task<BoardItem?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    Task<BoardItem> CreateItemAsync(BoardSection section, string title, Guid? itemId = null, CancellationToken cancellationToken = default);

    Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default);

    Task<BoardItem?> ArchiveItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default);

    Task<BoardItem?> UnarchiveItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default);

    Task<BoardSnapshot> GetArchivedSnapshotAsync(CancellationToken cancellationToken = default);

    Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Sets the daily's last completion to a specific past calendar day (UTC), for backdating completion.</summary>
    Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn,
        CancellationToken cancellationToken = default);

    Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default);

    Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default);

    Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        UpdateHabitArgs args,
        CancellationToken cancellationToken = default);

    Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        UpdateTodoArgs args,
        CancellationToken cancellationToken = default);

    Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        UpdateDailyArgs args,
        CancellationToken cancellationToken = default);

    Task<Dictionary<Guid, int>> GetStreakMapAsync(CancellationToken cancellationToken = default);
}
