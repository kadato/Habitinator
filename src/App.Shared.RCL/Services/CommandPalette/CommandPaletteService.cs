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
    private readonly BoardUiSessionState? _uiSession;
    private readonly IUserDayService? _userDay;
    private readonly List<string> _recentIds = [];
#pragma warning disable IDE0028 // Dictionary with comparer cannot use collection expression.
    private readonly Dictionary<string, CommandItem> _knownItems = new(StringComparer.Ordinal);
#pragma warning restore IDE0028
    private readonly Queue<string> _knownItemOrder = new();
    private const int MaxRecent = 5;
    private const int MaxKnownItems = 250;

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
        ITimerSessionLogService? timerSessionLog = null,
        BoardUiSessionState? uiSession = null,
        IUserTimeZoneService? timeZoneService = null,
        IUserDayService? userDay = null)
    {
        _boardData = boardData;
        Action close = Close;
        Func<Task> notifyRefresh = NotifyBoardRefreshAsync;
        _navigation = new NavigationPaletteCommands(nav, boardData, dialogs, preferences, notifier, close, notifyRefresh, uiSession, timeZoneService, userDay);
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
            notifyRefresh,
            uiSession,
            userDay);
        _timers = new TimerPaletteCommands(
            timer, boardData, dialogs, notifier, timerSessionLog, _navigation.NavigateAsync, close, notifyRefresh);
        _exportCommands = new ExportPaletteCommands(export, boardData, js, dialogs, notifier, close);
        _refresh = refresh;
        _uiSession = uiSession;
        _userDay = userDay;
    }

    private Task NotifyBoardRefreshAsync()
    {
        if (_refresh is not null)
        {
            // Palette mutations are local-first and already queued a sync.
            // Refreshing the other subscribers must not delay the command
            // result or the next command.
            _ = RefreshInBackgroundAsync();
        }

        return Task.CompletedTask;
    }

    private async Task RefreshInBackgroundAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _refresh!.NotifyFromRemoteAsync(cts.Token);
        }
        catch
        {
            // Best effort board refresh.
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
        _navigation.AddTagManagerCommand(list);
        _navigation.AddHelpCommand(list);
        _board.AddUndoCommand(list);
        _timers.AddTimerActionCommands(list);
        _navigation.AddYesterdayRetroCommand(list);
        _exportCommands.AddExportCommand(list);
        _navigation.AddAppearanceCommands(list);
        _navigation.AddAiPromptCommands(list);

        foreach (var c in list)
        {
            RememberCommand(c);
        }

        return Task.FromResult(list);
    }

    public IReadOnlyList<string> RecentIds => _recentIds;

    internal int KnownItemCount => _knownItems.Count;

    public void RecordRecent(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            return;
        }

        _recentIds.Remove(commandId);
        _recentIds.Insert(0, commandId);
        while (_recentIds.Count > MaxRecent)
        {
            _recentIds.RemoveAt(_recentIds.Count - 1);
        }
    }

    public void SetRecentIds(IEnumerable<string>? ids)
    {
        _recentIds.Clear();
        if (ids is null)
        {
            return;
        }

        foreach (var id in ids.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().Take(MaxRecent))
        {
            _recentIds.Add(id);
        }
    }

    public List<CommandItem> BuildRecentGroup(List<CommandItem> rootCommands)
    {
        foreach (var c in rootCommands)
        {
            RememberCommand(c);
        }

        var recent = new List<CommandItem>();
        foreach (var id in _recentIds)
        {
            var match = rootCommands.FirstOrDefault(c => c.Id == id);
            if (match is not null)
            {
                recent.Add(match with { Category = "Recent" });
            }
            else if (_knownItems.TryGetValue(id, out var known))
            {
                recent.Add(known with { Category = "Recent" });
            }
        }

        return recent;
    }

    private void RememberCommand(CommandItem item)
    {
        if (!_knownItems.ContainsKey(item.Id))
        {
            _knownItemOrder.Enqueue(item.Id);
        }

        _knownItems[item.Id] = item;

        // Search results add item-specific commands on every query. Keep the
        // recent-command fallback bounded so a long session cannot grow it forever.
        while (_knownItems.Count > MaxKnownItems && _knownItemOrder.TryDequeue(out var oldest))
        {
            _knownItems.Remove(oldest);
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength - 1).TrimEnd(), "…");
    }

    public List<CommandItem> BuildQuickAddCommands(string query)
    {
        var q = query.Trim();
        if (!q.StartsWith("add ", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var rest = q[4..].Trim();
        if (string.IsNullOrWhiteSpace(rest))
        {
            return [];
        }

        var section = BoardSection.Todo;
        var title = rest;
        var lower = rest.ToLowerInvariant();
        if (lower.StartsWith("todo ", StringComparison.Ordinal) || lower.StartsWith("to-do ", StringComparison.Ordinal))
        {
            section = BoardSection.Todo;
            title = rest[(rest.IndexOf(' ') + 1)..].Trim();
        }
        else if (lower.StartsWith("habit ", StringComparison.Ordinal))
        {
            section = BoardSection.Habit;
            title = rest[6..].Trim();
        }
        else if (lower.StartsWith("daily ", StringComparison.Ordinal) || lower.StartsWith("dailies ", StringComparison.Ordinal))
        {
            section = BoardSection.Daily;
            title = rest[(rest.IndexOf(' ') + 1)..].Trim();
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var (parsedTitle, tags, dueDate) = BoardPaletteCommands.ParseInlineTitle(
            title,
            _userDay?.Today ?? DailySchedule.LocalToday());
        var preview = parsedTitle;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(tags))
        {
            parts.Add($"#{tags.Replace(", ", " #")}");
        }
        if (dueDate.HasValue)
        {
            parts.Add($"Due: {dueDate.Value:yyyy-MM-dd}");
        }

        var subtitle = parts.Count > 0
            ? $"Quick add ({string.Join(", ", parts)})"
            : "Quick add without opening the editor";

        var capturedSection = section;
        var capturedTitle = title;
        return
        [
            new CommandItem(
                Id: $"quick-add-{capturedSection}-{preview.GetHashCode(StringComparison.Ordinal):x}",
                Title: $"Add {capturedSection}: {preview}",
                Subtitle: subtitle,
                Category: "Quick add",
                Icon: Icons.Material.Filled.Add,
                Action: () => _board.CreateItemWithTitleAsync(capturedSection, capturedTitle),
                Keywords: ["add", "quick", "create", capturedSection.ToString().ToLowerInvariant()])
        ];
    }

    public CommandItem? BuildAskAiCommand(string query)
    {
        var q = query.Trim();
        if (string.IsNullOrWhiteSpace(q) || q.StartsWith('>'))
        {
            return null;
        }

        if (_uiSession is null)
        {
            return null;
        }

        var prompt = q;
        return new CommandItem(
            Id: $"ask-ai-{prompt.GetHashCode(StringComparison.Ordinal):x}",
            Title: $"Ask AI about \"{Truncate(prompt, 48)}\"",
            Subtitle: "Open the assistant with this question",
            Category: "AI assistant",
            Icon: Icons.Material.Filled.SmartToy,
            Action: () =>
            {
                Close();
                _uiSession.PendingAiPrompt = prompt;
                _uiSession.SetAiDrawer(true);
                return Task.CompletedTask;
            },
            Keywords: ["ai", "ask", "assistant", "chat"]);
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
            if (q.StartsWith('>'))
            {
                // Command-only mode: skip board search so ">" filters root commands.
                return results;
            }

            if (_board.TryBuildDeleteCommands(q, snapshot) is { } deletes)
            {
                // Delete intent first, but keep timer shortcuts and item matches below it.
                results.AddRange(deletes);
            }

            _timers.AddTimerShortcutCommands(q, results);
            _board.AddItemMatchCommands(q, snapshot, results);

            foreach (var r in results)
            {
                RememberCommand(r);
            }
        }
        catch
        {
            // Best effort search.
        }

        return results;
    }

    public Task CreateItemAsync(BoardSection section) =>
        _board.CreateItemAsync(section);

    public Task CreateItemWithTitleAsync(BoardSection section, string title) =>
        _board.CreateItemWithTitleAsync(section, title);

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

    public Task ShowHelpAsync() =>
        _navigation.ShowHelpAsync();

    public Task StopTimerSessionAsync() =>
        _timers.StopTimerSessionAsync();

    public Task TogglePomodoroModeAsync() =>
        _timers.TogglePomodoroModeAsync();

    public Task StartPomodoroSessionAsync() =>
        _timers.StartPomodoroSessionAsync();
}
#pragma warning restore S107
