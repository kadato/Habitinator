namespace App.Shared.RCL.Services;

public sealed record BoardColumnFilterState(
    string? HabitFilter,
    string? DailyFilter,
    string? TodoFilter,
    bool? TodoSortDueSoon);

public interface IBoardColumnStateStore
{
    Task<BoardColumnFilterState?> GetAsync(CancellationToken cancellationToken = default);

    Task SetAsync(BoardColumnFilterState state, CancellationToken cancellationToken = default);
}
