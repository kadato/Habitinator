using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public interface IBoardDataService
{
    Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<BoardItem> CreateItemAsync(BoardSection section, string title, CancellationToken cancellationToken = default);

    Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title, CancellationToken cancellationToken = default);

    Task<bool> DeleteItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default);

    Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default);

    Task<BoardItem?> IncrementHabitAsync(Guid itemId, CancellationToken cancellationToken = default);
}
