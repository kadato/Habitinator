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

    IReadOnlyList<string> RecentIds { get; }

    void RecordRecent(string commandId);

    void SetRecentIds(IEnumerable<string>? ids);

    List<CommandItem> BuildRecentGroup(List<CommandItem> rootCommands);

    List<CommandItem> BuildQuickAddCommands(string query);

    CommandItem? BuildAskAiCommand(string query);

    Task CreateItemAsync(BoardSection section);

    Task CreateItemWithTitleAsync(BoardSection section, string title);

    Task DeleteItemAsync(BoardSection section, Guid itemId);

    Task DeleteCompletedTodosAsync();

    Task ArchiveCompletedTodosAsync();

    Task ExportDataAsync();

    Task UndoAsync();

    Task StopTimerSessionAsync();

    Task TogglePomodoroModeAsync();

    Task StartPomodoroSessionAsync();
}
