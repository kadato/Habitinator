using App.Shared.RCL.Components;
using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace App.Shared.RCL.Services.CommandPalette;

public sealed class CommandPaletteService : ICommandPaletteService
{
    private readonly NavigationManager _nav;
    private readonly IBoardDataService _boardData;
    private readonly GlobalTimerService _timer;
    private readonly IUndoService _undo;
    private readonly IDialogService _dialogs;
    private readonly IUserPreferencesService _preferences;
    private readonly IUserNotifier _notifier;
    private readonly IRemoteBoardRefreshService? _refresh;

    public CommandPaletteService(
        NavigationManager nav,
        IBoardDataService boardData,
        GlobalTimerService timer,
        IUndoService undo,
        IDialogService dialogs,
        IUserPreferencesService preferences,
        IUserNotifier notifier,
        IRemoteBoardRefreshService? refresh = null)
    {
        _nav = nav;
        _boardData = boardData;
        _timer = timer;
        _undo = undo;
        _dialogs = dialogs;
        _preferences = preferences;
        _notifier = notifier;
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
                // Best-effort board refresh
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
        var list = new List<CommandItem>
        {
            // 1. Suggested / Quick Create
            new(
                Id: "create-todo",
                Title: "New To-do",
                Subtitle: "Add a single task to your board",
                Category: "Suggested",
                Icon: Icons.Material.Filled.CheckBoxOutlineBlank,
                ShortcutBadge: "Alt+T",
                Action: () => CreateItemAsync(BoardSection.Todo),
                Keywords: ["todo", "task", "create", "new", "add"]),

            new(
                Id: "create-habit",
                Title: "New Habit",
                Subtitle: "Add a countable positive or negative habit",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Repeat,
                ShortcutBadge: "Ctrl+H",
                Action: () => CreateItemAsync(BoardSection.Habit),
                Keywords: ["habit", "create", "new", "add", "streak"]),

            new(
                Id: "create-daily",
                Title: "New Daily",
                Subtitle: "Add a recurring daily habit",
                Category: "Suggested",
                Icon: Icons.Material.Filled.CalendarToday,
                ShortcutBadge: "Ctrl+D",
                Action: () => CreateItemAsync(BoardSection.Daily),
                Keywords: ["daily", "recurring", "schedule", "create", "new", "add"])
        };

        // Timer action based on current state
        if (_timer.IsRunning)
        {
            list.Add(new(
                Id: "timer-pause",
                Title: "Pause Focus Timer",
                Subtitle: "Temporarily pause the running session",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Pause,
                ShortcutBadge: "S",
                Action: () =>
                {
                    Close();
                    _timer.Pause();
                    return Task.CompletedTask;
                },
                Keywords: ["timer", "pause", "stopwatch", "focus"]));
        }
        else
        {
            list.Add(new(
                Id: "timer-start",
                Title: "Start Focus Session",
                Subtitle: "Start tracking time on the global timer",
                Category: "Suggested",
                Icon: Icons.Material.Filled.PlayArrow,
                ShortcutBadge: "S",
                Action: () =>
                {
                    Close();
                    _timer.Start();
                    return Task.CompletedTask;
                },
                Keywords: ["timer", "start", "focus", "pomodoro", "stopwatch"]));
        }

        // 2. Navigation
        list.Add(new(
            Id: "nav-board",
            Title: "Board",
            Subtitle: "Overview of habits, dailies, and to-dos",
            Category: "Navigation",
            Icon: Icons.Material.Filled.SpaceDashboard,
            ShortcutBadge: "G B",
            Action: () => NavigateAsync("/"),
            Keywords: ["board", "home", "main", "habits", "dailies", "todos"]));

        list.Add(new(
            Id: "nav-stats",
            Title: "Statistics",
            Subtitle: "Activity heatmap, history, and completions",
            Category: "Navigation",
            Icon: Icons.Material.Filled.BarChart,
            ShortcutBadge: "G S",
            Action: () => NavigateAsync("/stats"),
            Keywords: ["stats", "statistics", "charts", "history", "analytics"]));

        list.Add(new(
            Id: "nav-settings",
            Title: "Settings",
            Subtitle: "Preferences, appearance, notifications",
            Category: "Navigation",
            Icon: Icons.Material.Filled.Settings,
            ShortcutBadge: "G P",
            Action: () => NavigateAsync("/settings"),
            Keywords: ["settings", "preferences", "config", "account", "profile"]));

        // 3. Actions
        list.Add(new(
            Id: "action-archive",
            Title: "Open Archive",
            Subtitle: "Browse and restore archived board items",
            Category: "Actions",
            Icon: Icons.Material.Filled.Archive,
            Action: OpenArchiveAsync,
            Keywords: ["archive", "archived", "restore", "history"]));

        list.Add(new(
            Id: "action-undo",
            Title: "Undo Last Action",
            Subtitle: "Revert the most recent change",
            Category: "Actions",
            Icon: Icons.Material.Filled.Undo,
            ShortcutBadge: "Ctrl+Z",
            Action: UndoAsync,
            Keywords: ["undo", "revert"]));

        list.Add(new(
            Id: "timer-reset",
            Title: "Reset Focus Timer",
            Subtitle: "Reset the session timer to zero",
            Category: "Actions",
            Icon: Icons.Material.Filled.Refresh,
            Action: () =>
            {
                Close();
                _timer.Reset();
                return Task.CompletedTask;
            },
            Keywords: ["timer", "reset", "clear"]));

        // 4. Appearance
        list.Add(new(
            Id: "theme-dark",
            Title: "Dark Theme",
            Subtitle: "Switch app appearance to dark mode",
            Category: "Appearance",
            Icon: Icons.Material.Filled.DarkMode,
            Action: () => SetThemeAsync(AppTheme.Dark),
            Keywords: ["theme", "dark", "mode", "color", "night"]));

        list.Add(new(
            Id: "theme-light",
            Title: "Light Theme",
            Subtitle: "Switch app appearance to light mode",
            Category: "Appearance",
            Icon: Icons.Material.Filled.LightMode,
            Action: () => SetThemeAsync(AppTheme.Light),
            Keywords: ["theme", "light", "mode", "color", "day"]));

        list.Add(new(
            Id: "theme-system",
            Title: "System Theme",
            Subtitle: "Sync app appearance with operating system",
            Category: "Appearance",
            Icon: Icons.Material.Filled.SettingsBrightness,
            Action: () => SetThemeAsync(AppTheme.System),
            Keywords: ["theme", "system", "auto", "os"]));

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

            // Habits
            var matchingHabits = snapshot.Habits
                .Where(h => Matches(h.Title, h.Notes, h.Tags, q))
                .Take(4);

            foreach (var h in matchingHabits)
            {
                results.Add(new CommandItem(
                    Id: $"habit-{h.Id}",
                    Title: h.Title,
                    Subtitle: $"Habit • +{h.Counter} / -{h.NegativeCounter}",
                    Category: "Habits",
                    Icon: Icons.Material.Filled.Repeat,
                    ChildrenProvider: () => Task.FromResult(GetHabitSubActions(h))));
            }

            // Dailies
            var matchingDailies = snapshot.Dailies
                .Where(d => Matches(d.Title, d.Notes, d.Tags, q))
                .Take(4);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var d in matchingDailies)
            {
                var isDone = d.DailyLastCompletedOn.HasValue &&
                             d.DailyLastCompletedOn.Value == today;
                results.Add(new CommandItem(
                    Id: $"daily-{d.Id}",
                    Title: d.Title,
                    Subtitle: isDone ? $"Daily • Completed today (Streak: {d.Counter})" : $"Daily • Streak: {d.Counter}",
                    Category: "Dailies",
                    Icon: isDone ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CalendarToday,
                    ChildrenProvider: () => Task.FromResult(GetDailySubActions(d, isDone))));
            }

            // To-dos
            var matchingTodos = snapshot.Todos
                .Where(t => Matches(t.Title, t.Notes, t.Tags, q))
                .Take(4);

            foreach (var t in matchingTodos)
            {
                string status;
                if (t.IsCompleted)
                {
                    status = "Completed";
                }
                else if (t.TodoDueDate.HasValue)
                {
                    status = $"Due {t.TodoDueDate.Value:MMM d}";
                }
                else
                {
                    status = "Open";
                }

                results.Add(new CommandItem(
                    Id: $"todo-{t.Id}",
                    Title: t.Title,
                    Subtitle: $"To-do • {status}",
                    Category: "To-dos",
                    Icon: t.IsCompleted ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CheckBoxOutlineBlank,
                    ChildrenProvider: () => Task.FromResult(GetTodoSubActions(t))));
            }
        }
        catch
        {
            // best-effort search
        }

        return results;
    }

    private List<CommandItem> GetHabitSubActions(BoardItem h) =>
    [
        new(
            Id: $"habit-{h.Id}-plus",
            Title: "+1 Increment Count",
            Subtitle: "Log positive completion",
            Category: "Actions",
            Icon: Icons.Material.Filled.Add,
            Action: async () =>
            {
                Close();
                await _boardData.IncrementHabitPlusAsync(h.Id);
                await NotifyBoardRefreshAsync();
            }),
        new(
            Id: $"habit-{h.Id}-minus",
            Title: "-1 Decrement Count",
            Subtitle: "Log setback count",
            Category: "Actions",
            Icon: Icons.Material.Filled.Remove,
            Action: async () =>
            {
                Close();
                await _boardData.IncrementHabitMinusAsync(h.Id);
                await NotifyBoardRefreshAsync();
            }),
        new(
            Id: $"habit-{h.Id}-timer",
            Title: "Start Focus Timer on this Habit",
            Subtitle: "Set timer target and start stopwatch",
            Category: "Actions",
            Icon: Icons.Material.Filled.Timer,
            Action: () =>
            {
                Close();
                _timer.SelectTarget("Habit", h.Title, h.Id);
                _timer.Start();
                return Task.CompletedTask;
            }),
        new(
            Id: $"habit-{h.Id}-edit",
            Title: "Edit Habit Details",
            Subtitle: "Open editor for title, notes, and checklist",
            Category: "Actions",
            Icon: Icons.Material.Filled.Edit,
            Action: async () =>
            {
                Close();
                var parameters = new DialogParameters<EditHabitDialog> { { x => x.Item, h } };
                var dialog = await _dialogs.ShowAsync<EditHabitDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
                await dialog.Result;
                await NotifyBoardRefreshAsync();
            }),
        new(
            Id: $"habit-{h.Id}-archive",
            Title: "Archive Habit",
            Subtitle: "Move item to archive",
            Category: "Actions",
            Icon: Icons.Material.Filled.Archive,
            Action: async () =>
            {
                Close();
                await _boardData.ArchiveItemAsync(BoardSection.Habit, h.Id);
                await NotifyBoardRefreshAsync();
            })
    ];

    private List<CommandItem> GetDailySubActions(BoardItem d, bool isDone) =>
    [
        new(
            Id: $"daily-{d.Id}-toggle",
            Title: isDone ? "Unmark Completed" : "Mark Completed Today",
            Subtitle: isDone ? "Reopen this daily for today" : "Record daily streak progression",
            Category: "Actions",
            Icon: isDone ? Icons.Material.Filled.RadioButtonUnchecked : Icons.Material.Filled.CheckCircle,
            Action: async () =>
            {
                Close();
                await _boardData.ToggleItemAsync(BoardSection.Daily, d.Id);
                await NotifyBoardRefreshAsync();
            }),
        new(
            Id: $"daily-{d.Id}-timer",
            Title: "Start Focus Timer on this Daily",
            Subtitle: "Set timer target and start stopwatch",
            Category: "Actions",
            Icon: Icons.Material.Filled.Timer,
            Action: () =>
            {
                Close();
                _timer.SelectTarget("Daily", d.Title, d.Id);
                _timer.Start();
                return Task.CompletedTask;
            }),
        new(
            Id: $"daily-{d.Id}-edit",
            Title: "Edit Daily Details",
            Subtitle: "Open editor for schedule, checklist, and repeat settings",
            Category: "Actions",
            Icon: Icons.Material.Filled.Edit,
            Action: async () =>
            {
                Close();
                var parameters = new DialogParameters<EditDailyDialog> { { x => x.Item, d } };
                var dialog = await _dialogs.ShowAsync<EditDailyDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
                await dialog.Result;
                await NotifyBoardRefreshAsync();
            }),
        new(
            Id: $"daily-{d.Id}-archive",
            Title: "Archive Daily",
            Subtitle: "Move item to archive",
            Category: "Actions",
            Icon: Icons.Material.Filled.Archive,
            Action: async () =>
            {
                Close();
                await _boardData.ArchiveItemAsync(BoardSection.Daily, d.Id);
                await NotifyBoardRefreshAsync();
            })
    ];

    private List<CommandItem> GetTodoSubActions(BoardItem t) =>
    [
        new(
            Id: $"todo-{t.Id}-toggle",
            Title: t.IsCompleted ? "Mark as Incomplete" : "Mark as Done",
            Subtitle: t.IsCompleted ? "Reopen this to-do" : "Complete task",
            Category: "Actions",
            Icon: t.IsCompleted ? Icons.Material.Filled.RadioButtonUnchecked : Icons.Material.Filled.CheckCircle,
            Action: async () =>
            {
                Close();
                await _boardData.ToggleItemAsync(BoardSection.Todo, t.Id);
                await NotifyBoardRefreshAsync();
            }),
        new(
            Id: $"todo-{t.Id}-timer",
            Title: "Start Focus Timer on this To-do",
            Subtitle: "Set timer target and start stopwatch",
            Category: "Actions",
            Icon: Icons.Material.Filled.Timer,
            Action: () =>
            {
                Close();
                _timer.SelectTarget("Todo", t.Title, t.Id);
                _timer.Start();
                return Task.CompletedTask;
            }),
        new(
            Id: $"todo-{t.Id}-edit",
            Title: "Edit To-do Details",
            Subtitle: "Open editor for title, notes, and checklist",
            Category: "Actions",
            Icon: Icons.Material.Filled.Edit,
            Action: async () =>
            {
                Close();
                var parameters = new DialogParameters<EditTodoDialog> { { x => x.Item, t } };
                var dialog = await _dialogs.ShowAsync<EditTodoDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
                await dialog.Result;
                await NotifyBoardRefreshAsync();
            }),
        new(
            Id: $"todo-{t.Id}-archive",
            Title: "Archive To-do",
            Subtitle: "Move item to archive",
            Category: "Actions",
            Icon: Icons.Material.Filled.Archive,
            Action: async () =>
            {
                Close();
                await _boardData.ArchiveItemAsync(BoardSection.Todo, t.Id);
                await NotifyBoardRefreshAsync();
            })
    ];

    private static bool Matches(string title, string? notes, string? tags, string query)
    {
        if (title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (notes is not null && notes.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (tags is not null && tags.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private bool _isCreatingItem;

    public async Task CreateItemAsync(BoardSection section)
    {
        if (_isCreatingItem)
        {
            return;
        }

        _isCreatingItem = true;
        Close();
        var defaultTitle = section switch
        {
            BoardSection.Habit => "New Habit",
            BoardSection.Daily => "New Daily",
            BoardSection.Todo => "New To-do",
            _ => "New Item"
        };
        try
        {
            var item = await _boardData.CreateItemAsync(section, defaultTitle);
            await NotifyBoardRefreshAsync();
            if (item is not null)
            {
                var options = DialogDefaults.SmallEditor;
                var dialog = section switch
                {
                    BoardSection.Habit => await _dialogs.ShowAsync<EditHabitDialog>(
                        string.Empty, new DialogParameters<EditHabitDialog> { { x => x.Item, item } }, options),
                    BoardSection.Daily => await _dialogs.ShowAsync<EditDailyDialog>(
                        string.Empty, new DialogParameters<EditDailyDialog> { { x => x.Item, item } }, options),
                    _ => await _dialogs.ShowAsync<EditTodoDialog>(
                        string.Empty, new DialogParameters<EditTodoDialog> { { x => x.Item, item } }, options)
                };

                var result = await dialog.Result;
                if (result is { Canceled: false, Data: not null })
                {
                    EditDialogAction? action = result.Data switch
                    {
                        EditHabitDialogResult h => h.Action,
                        EditDailyDialogResult d => d.Action,
                        EditTodoDialogResult t => t.Action,
                        _ => null
                    };

                    if (action == EditDialogAction.Archive)
                    {
                        await _boardData.ArchiveItemAsync(section, item.Id);
                    }
                    else if (action == EditDialogAction.Delete)
                    {
                        await _boardData.DeleteItemAsync(section, item.Id);
                    }
                }

                // If unchanged, prune empty item
                var current = await _boardData.GetItemAsync(item.Id);
                if (current is not null && current == item)
                {
                    await _boardData.DeleteItemAsync(section, item.Id);
                }

                await NotifyBoardRefreshAsync();
            }
        }
        catch
        {
            await _notifier.NotifyAsync("Could not create item.", Severity.Error);
        }
        finally
        {
            _isCreatingItem = false;
        }
    }

    private Task NavigateAsync(string path)
    {
        Close();
        _nav.NavigateTo(path);
        return Task.CompletedTask;
    }

    private async Task OpenArchiveAsync()
    {
        Close();
        await _dialogs.ShowAsync<ArchivedItemsDialog>(string.Empty, DialogDefaults.Wide);
    }

    public async Task UndoAsync()
    {
        Close();
        if (_undo.CanUndo)
        {
            await _undo.UndoAsync();
            await NotifyBoardRefreshAsync();
        }
        else
        {
            await _notifier.NotifyAsync("Nothing to undo.", Severity.Info);
        }
    }

    private async Task SetThemeAsync(AppTheme theme)
    {
        Close();
        try
        {
            var prefs = await _preferences.GetAsync();
            if (prefs.Theme != theme)
            {
                prefs.Theme = theme;
                await _preferences.SaveAsync(prefs);
                await _notifier.NotifyAsync($"Theme set to {theme}.", Severity.Success);
            }
        }
        catch
        {
            // best-effort theme update
        }
    }
}
