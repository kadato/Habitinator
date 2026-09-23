using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services.CommandPalette;

public interface ICommandPaletteService
{
    bool IsOpen { get; }

    event Action? StateChanged;

    void Open();

    void Close();

    void Toggle();

    Task<List<CommandItem>> GetRootCommandsAsync();

    Task<List<CommandItem>> SearchBoardItemsAsync(string query, CancellationToken cancellationToken = default);

    Task CreateItemAsync(BoardSection section);

    Task DeleteItemAsync(BoardSection section, Guid itemId);

    Task DeleteCompletedTodosAsync();

    Task ArchiveCompletedTodosAsync();

    Task ExportDataAsync();

    Task UndoAsync();

    Task StopTimerSessionAsync();

    Task TogglePomodoroModeAsync();

    Task StartPomodoroSessionAsync();
}
