using App.Shared.RCL.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Services.CommandPalette;

/// <summary>
/// Command palette entry point. State and cross-area composition live here;
/// board, timer, navigation, and export commands live in their own classes.
/// </summary>
#pragma warning disable S107 // Palette aggregator wires board, timer, navigation, and export collaborators; splitting it would scatter one composition root.
public sealed class CommandPaletteService : ICommandPaletteService
{
    private readonly IBoardDataService _boardData;
    private readonly BoardPaletteCommands _board;
    private readonly TimerPaletteCommands _timers;
    private readonly NavigationPaletteCommands _navigation;
    private readonly ExportPaletteCommands _exportCommands;
    private readonly IRemoteBoardRefreshService? _refresh;

    public CommandPaletteService(
        NavigationManager nav,
        IBoardDataService boardData,
        GlobalTimerService timer,
        IUndoService undo,
        IDialogService dialogs,
        IUserPreferencesService preferences,
        IUserNotifier notifier,
        IRemoteBoardRefreshService? refresh = null,
        IUserDataExportService? export = null,
        IJSRuntime? js = null,
        ITimerSessionLogService? timerSessionLog = null)
    {
        _boardData = boardData;
        Action close = Close;
        Func<Task> notifyRefresh = NotifyBoardRefreshAsync;
        _navigation = new NavigationPaletteCommands(nav, boardData, dialogs, preferences, notifier, close, notifyRefresh);
        _board = new BoardPaletteCommands(
            boardData,
            undo,
            dialogs,
            notifier,
            (type, title, id) =>
            {
                timer.SelectTarget(type, title, id);
                timer.Start();
            },
            close,
            notifyRefresh);
        _timers = new TimerPaletteCommands(
            timer, boardData, dialogs, notifier, timerSessionLog, _navigation.NavigateAsync, close, notifyRefresh);
        _exportCommands = new ExportPaletteCommands(export, boardData, js, dialogs, notifier, close);
        _refresh = refresh;
    }

    private async Task NotifyBoardRefreshAsync()
    {
        if (_refresh is not null)
        {
            try
            {
                await _refresh.NotifyFromRemoteAsync();
            }
            catch
            {
                // Best effort board refresh.
            }
        }
    }

    public bool IsOpen { get; private set; }

    public event Action? StateChanged;

    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        StateChanged?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        StateChanged?.Invoke();
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        StateChanged?.Invoke();
    }

    public Task<List<CommandItem>> GetRootCommandsAsync()
    {
        var list = new List<CommandItem>();
        list.AddRange(_board.GetSuggestedCreateCommands());
        _timers.AddSuggestedTimerCommands(list);
        _navigation.AddNavigationCommands(list);
        _board.AddBoardCleanupCommands(list);
        _navigation.AddArchiveCommand(list);
        _board.AddUndoCommand(list);
        _timers.AddTimerActionCommands(list);
        _navigation.AddYesterdayRetroCommand(list);
        _exportCommands.AddExportCommand(list);
        _navigation.AddAppearanceCommands(list);
        return Task.FromResult(list);
    }

    public async Task<List<CommandItem>> SearchBoardItemsAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<CommandItem>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        try
        {
            var snapshot = await _boardData.GetSnapshotAsync(cancellationToken);
            var q = query.Trim();
            if (_board.TryBuildDeleteCommands(q, snapshot) is { } deletes)
            {
                return deletes;
            }

            _timers.AddTimerShortcutCommands(q, results);
            _board.AddItemMatchCommands(q, snapshot, results);
        }
        catch
        {
            // Best effort search.
        }

        return results;
    }

    public Task CreateItemAsync(BoardSection section) =>
        _board.CreateItemAsync(section);

    public Task DeleteItemAsync(BoardSection section, Guid itemId) =>
        _board.DeleteItemAsync(section, itemId);

    public Task DeleteCompletedTodosAsync() =>
        _board.DeleteCompletedTodosAsync();

    public Task ArchiveCompletedTodosAsync() =>
        _board.ArchiveCompletedTodosAsync();

    public Task ExportDataAsync() =>
        _exportCommands.ExportDataAsync();

    public Task UndoAsync() =>
        _board.UndoAsync();

    public Task StopTimerSessionAsync() =>
        _timers.StopTimerSessionAsync();

    public Task TogglePomodoroModeAsync() =>
        _timers.TogglePomodoroModeAsync();

    public Task StartPomodoroSessionAsync() =>
        _timers.StartPomodoroSessionAsync();
}
#pragma warning restore S107
