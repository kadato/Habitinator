using App.Shared.RCL.Components;
using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

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
    private readonly IUserDataExportService? _export;
    private readonly IJSRuntime? _js;
    private readonly ITimerSessionLogService? _timerSessionLog;

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
        _nav = nav;
        _boardData = boardData;
        _timer = timer;
        _undo = undo;
        _dialogs = dialogs;
        _preferences = preferences;
        _notifier = notifier;
        _refresh = refresh;
        _export = export;
        _js = js;
        _timerSessionLog = timerSessionLog;
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
        var list = new List<CommandItem>
        {
            // 1. Suggested / Quick Create
            new(
                Id: "create-todo",
                Title: "New to-do",
                Subtitle: "Add a single task to your board",
                Category: "Suggested",
                Icon: Icons.Material.Filled.CheckBoxOutlineBlank,
                ShortcutBadge: "Alt+T",
                Action: () => CreateItemAsync(BoardSection.Todo),
                Keywords: ["todo", "task", "create", "new", "add"]),

            new(
                Id: "create-habit",
                Title: "New habit",
                Subtitle: "Add a countable positive or negative habit",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Repeat,
                ShortcutBadge: "Ctrl+H",
                Action: () => CreateItemAsync(BoardSection.Habit),
                Keywords: ["habit", "create", "new", "add", "streak"]),

            new(
                Id: "create-daily",
                Title: "New daily",
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
                Id: "timer-stop",
                Title: "Stop focus session",
                Subtitle: $"Stop and log session of {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)}",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Stop,
                ShortcutBadge: "S",
                Action: StopTimerSessionAsync,
                Keywords: ["timer", "stop", "session", "virtual session", "focus", "log", "lock", "end"]));

            list.Add(new(
                Id: "timer-pause",
                Title: "Pause focus timer",
                Subtitle: _timer.PomodoroModeEnabled ? $"Pause active {_timer.StatusLabel}" : "Temporarily pause the running session",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Pause,
                Action: () =>
                {
                    Close();
                    _timer.Pause();
                    return Task.CompletedTask;
                },
                Keywords: ["timer", "pause", "session", "virtual session", "focus", "break"]));

            list.Add(new(
                Id: "timer-reset",
                Title: "Reset focus timer",
                Subtitle: _timer.PomodoroModeEnabled ? "Reset Pomodoro intervals and return to idle" : "Reset session timer to zero",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Refresh,
                Action: async () =>
                {
                    Close();
                    _timer.ResetSession();
                    await _notifier.NotifyAsync(_timer.PomodoroModeEnabled ? "Pomodoro session reset to idle." : "Stopwatch timer reset to zero.", Severity.Info);
                },
                Keywords: ["timer", "reset", "clear", "zero"]));

            if (_timer.PomodoroModeEnabled && (_timer.CurrentPomodoroState == PomodoroState.ShortBreak || _timer.CurrentPomodoroState == PomodoroState.LongBreak))
            {
                list.Add(new(
                    Id: "timer-skip-break",
                    Title: "Skip break and resume work",
                    Subtitle: "End break early and start next Pomodoro work interval",
                    Category: "Suggested",
                    Icon: Icons.Material.Filled.SkipNext,
                    Action: async () =>
                    {
                        Close();
                        _timer.TransitionToWork();
                        _timer.Start();
                        var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
                        await _notifier.NotifyAsync($"Break skipped. Work started for '{targetName}'.", Severity.Info);
                    },
                    Keywords: ["skip", "break", "work", "pomodoro"]));
            }
        }
        else if (_timer.PomodoroModeEnabled && (_timer.CurrentPomodoroState == PomodoroState.ShortBreak || _timer.CurrentPomodoroState == PomodoroState.LongBreak))
        {
            list.Add(new(
                Id: "timer-start-break",
                Title: "Start break countdown",
                Subtitle: $"Begin {_timer.StatusLabel}, {GlobalTimerService.FormatTimeSpan(_timer.FocusAlertAfter ?? _timer.ShortBreakDuration)}",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Coffee,
                ShortcutBadge: "S",
                Action: () =>
                {
                    Close();
                    _timer.Start();
                    return Task.CompletedTask;
                },
                Keywords: ["break", "start", "timer", "coffee", "rest"]));

            list.Add(new(
                Id: "timer-skip-break",
                Title: "Skip break and start work",
                Subtitle: "Skip break and start next Pomodoro work interval immediately",
                Category: "Suggested",
                Icon: Icons.Material.Filled.SkipNext,
                Action: async () =>
                {
                    Close();
                    _timer.TransitionToWork();
                    _timer.Start();
                    var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
                    await _notifier.NotifyAsync($"Break skipped. Work started for '{targetName}'.", Severity.Info);
                },
                Keywords: ["skip", "break", "work", "pomodoro"]));

            list.Add(new(
                Id: "timer-stop",
                Title: "Stop focus session",
                Subtitle: "Reset and end the active Pomodoro cycle",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Stop,
                Action: StopTimerSessionAsync,
                Keywords: ["timer", "stop", "session", "virtual session", "end", "log", "lock"]));

            list.Add(new(
                Id: "timer-reset",
                Title: "Reset focus timer",
                Subtitle: "Reset Pomodoro intervals and return to idle",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Refresh,
                Action: async () =>
                {
                    Close();
                    _timer.ResetSession();
                    await _notifier.NotifyAsync("Pomodoro session reset to idle.", Severity.Info);
                },
                Keywords: ["timer", "reset", "clear", "zero"]));
        }
        else if (_timer.Elapsed > TimeSpan.Zero || (_timer.PomodoroModeEnabled && _timer.CurrentPomodoroState != PomodoroState.Idle))
        {
            var resumeLabel = _timer.PomodoroModeEnabled
                ? $"Resume Pomodoro, {_timer.GetDisplayTime()} remaining"
                : $"Resume focus session, {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)} elapsed";

            list.Add(new(
                Id: "timer-resume",
                Title: "Resume focus session",
                Subtitle: resumeLabel,
                Category: "Suggested",
                Icon: Icons.Material.Filled.PlayArrow,
                ShortcutBadge: "S",
                Action: () =>
                {
                    Close();
                    _timer.Start();
                    return Task.CompletedTask;
                },
                Keywords: ["timer", "resume", "start", "focus", "session", "virtual session"]));

            list.Add(new(
                Id: "timer-stop",
                Title: "Stop focus session",
                Subtitle: $"Stop and log session of {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)}",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Stop,
                Action: StopTimerSessionAsync,
                Keywords: ["timer", "stop", "session", "virtual session", "focus", "log", "lock", "end"]));

            list.Add(new(
                Id: "timer-reset",
                Title: "Reset focus timer",
                Subtitle: _timer.PomodoroModeEnabled ? "Reset Pomodoro intervals and return to idle" : "Reset session timer to zero",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Refresh,
                Action: async () =>
                {
                    Close();
                    _timer.ResetSession();
                    await _notifier.NotifyAsync(_timer.PomodoroModeEnabled ? "Pomodoro session reset to idle." : "Stopwatch timer reset to zero.", Severity.Info);
                },
                Keywords: ["timer", "reset", "clear", "zero"]));
        }
        else
        {
            var targetDesc = string.IsNullOrWhiteSpace(_timer.TargetId)
                ? "empty session"
                : $"target: {_timer.TargetId}";

            list.Add(new(
                Id: "timer-setup-and-start",
                Title: "Start focus session...",
                Subtitle: "Choose Stopwatch or Pomodoro, then select target and duration",
                Category: "Suggested",
                Icon: Icons.Material.Filled.PlayCircleOutline,
                ChildrenProvider: GetTimerModeSelectionCommandsAsync,
                Keywords: ["timer", "session", "virtual session", "start", "target", "focus", "duration", "explore", "empty", "modraw", "pomodoro", "comodoro", "stopwatch"]));

            list.Add(new(
                Id: "timer-start-pomodoro",
                Title: "Quick start Pomodoro",
                Subtitle: $"Start {_timer.WorkDuration.TotalMinutes:0}m Pomodoro work interval, {targetDesc}",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Timelapse,
                ShortcutBadge: "P",
                Action: StartPomodoroSessionAsync,
                Keywords: ["pomodoro", "comodoro", "modraw", "quick start pomodoro", "start", "focus", "virtual session"]));

            list.Add(new(
                Id: "timer-start",
                Title: "Start Stopwatch session",
                Subtitle: $"Start open stopwatch focus session, {targetDesc}",
                Category: "Suggested",
                Icon: Icons.Material.Filled.PlayArrow,
                ShortcutBadge: "S",
                Action: async () =>
                {
                    Close();
                    _timer.PomodoroModeEnabled = false;
                    _timer.Start();
                    var tName = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
                    await _notifier.NotifyAsync($"Stopwatch session started for '{tName}'.", Severity.Info);
                },
                Keywords: ["timer", "start", "stopwatch", "open", "focus", "session"]));
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

        list.Add(new(
            Id: "nav-settings-account",
            Title: "Account and data settings",
            Subtitle: "Profile, export, password, and account management",
            Category: "Navigation",
            Icon: Icons.Material.Filled.ManageAccounts,
            Action: () => NavigateAsync("/settings"),
            Keywords: ["account", "password", "profile", "export", "backup"]));

        list.Add(new(
            Id: "nav-settings-notifications",
            Title: "Notification settings",
            Subtitle: "Reminders, alerts, and quiet hours",
            Category: "Navigation",
            Icon: Icons.Material.Filled.Notifications,
            Action: () => NavigateAsync("/settings"),
            Keywords: ["notifications", "alerts", "reminders", "sound"]));

        // 3. Actions
        list.Add(new(
            Id: "action-delete-completed-todos",
            Title: "Delete completed to-dos",
            Subtitle: "Permanently remove all completed to-do items",
            Category: "Actions",
            Icon: Icons.Material.Filled.DeleteSweep,
            IsDanger: true,
            Action: DeleteCompletedTodosAsync,
            Keywords: ["delete", "remove", "clear", "completed", "done", "todos", "clean", "trash"]));

        list.Add(new(
            Id: "action-archive-completed-todos",
            Title: "Archive completed to-dos",
            Subtitle: "Move all completed to-do tasks to archive",
            Category: "Actions",
            Icon: Icons.Material.Filled.Archive,
            Action: ArchiveCompletedTodosAsync,
            Keywords: ["archive", "completed", "done", "todos", "hide"]));

        list.Add(new(
            Id: "action-manage-items",
            Title: "Manage and delete items",
            Subtitle: "Browse active items to quickly delete, archive, or edit",
            Category: "Actions",
            Icon: Icons.Material.Filled.DeleteOutline,
            ChildrenProvider: GetManageItemsSubActionsAsync,
            Keywords: ["delete", "remove", "manage", "items", "browse", "habits", "dailies", "todos", "trash"]));

        list.Add(new(
            Id: "action-archive",
            Title: "Open archive",
            Subtitle: "Browse and restore archived board items",
            Category: "Actions",
            Icon: Icons.Material.Filled.Archive,
            Action: OpenArchiveAsync,
            Keywords: ["archive", "archived", "restore", "history"]));

        list.Add(new(
            Id: "action-undo",
            Title: "Undo last action",
            Subtitle: "Revert the most recent change",
            Category: "Actions",
            Icon: Icons.Material.Filled.Undo,
            ShortcutBadge: "Ctrl+Z",
            Action: UndoAsync,
            Keywords: ["undo", "revert"]));

        if (_timer.IsRunning || _timer.Elapsed > TimeSpan.Zero || (_timer.PomodoroModeEnabled && _timer.CurrentPomodoroState != PomodoroState.Idle))
        {
            list.Add(new(
                Id: "timer-setup-and-start",
                Title: "Start new focus session...",
                Subtitle: "Choose Stopwatch or Pomodoro mode, then select target and duration",
                Category: "Actions",
                Icon: Icons.Material.Filled.PlayCircleOutline,
                ChildrenProvider: GetTimerModeSelectionCommandsAsync,
                Keywords: ["timer", "session", "virtual session", "start", "target", "focus", "duration", "explore", "empty", "modraw", "pomodoro", "comodoro", "stopwatch", "habit", "daily", "todo"]));
        }

        var targetDisplay = string.IsNullOrWhiteSpace(_timer.TargetId) ? "None" : _timer.TargetId;
        list.Add(new(
            Id: "timer-set-target",
            Title: "Set session target...",
            Subtitle: $"Target: {targetDisplay} - Assign habit, daily, to-do, or custom label",
            Category: "Actions",
            Icon: Icons.Material.Filled.AdsClick,
            ChildrenProvider: GetTimerTargetConfigurationCommandsAsync,
            Keywords: ["timer", "target", "focus", "item", "habit", "daily", "todo", "custom target", "label", "set target"]));

        string durationSubtitle;
        if (_timer.PomodoroModeEnabled)
        {
            durationSubtitle = $"Pomodoro mode active with {GlobalTimerService.FormatTimeSpan(_timer.WorkDuration)} interval. Select to configure.";
        }
        else if (_timer.FocusAlertAfter.HasValue)
        {
            durationSubtitle = $"Current alert: {GlobalTimerService.FormatTimeSpan(_timer.FocusAlertAfter.Value)}";
        }
        else
        {
            durationSubtitle = "Continuous count-up without alert";
        }

        list.Add(new(
            Id: "timer-set-duration",
            Title: "Set focus duration...",
            Subtitle: durationSubtitle,
            Category: "Actions",
            Icon: Icons.Material.Filled.HourglassTop,
            ChildrenProvider: GetDurationConfigurationCommandsAsync,
            Keywords: ["timer", "duration", "alert", "time's up", "focus", "minutes", "length", "custom duration"]));

        list.Add(new(
            Id: "timer-reset",
            Title: "Reset focus timer",
            Subtitle: _timer.PomodoroModeEnabled ? "Reset Pomodoro intervals and return to idle" : "Reset session timer to zero",
            Category: "Actions",
            Icon: Icons.Material.Filled.Refresh,
            Action: async () =>
            {
                Close();
                _timer.ResetSession();
                await _notifier.NotifyAsync("Focus timer reset.", Severity.Info);
            },
            Keywords: ["timer", "reset", "clear"]));

        if (_timer.TargetId is not null)
        {
            list.Add(new(
                Id: "timer-clear-target",
                Title: "Clear timer target",
                Subtitle: $"Active target: {_timer.TargetId}",
                Category: "Actions",
                Icon: Icons.Material.Filled.TimerOff,
                Action: async () =>
                {
                    Close();
                    _timer.SetManualTarget(null);
                    await _notifier.NotifyAsync("Timer target cleared.", Severity.Info);
                },
                Keywords: ["timer", "clear target", "target", "stop"]));
        }

        list.Add(new(
            Id: "timer-toggle-pomodoro",
            Title: _timer.PomodoroModeEnabled ? "Disable Pomodoro mode" : "Enable Pomodoro mode",
            Subtitle: _timer.PomodoroModeEnabled ? "Switch to open Stopwatch focus mode" : "Switch to interval focus sessions with scheduled breaks",
            Category: "Actions",
            Icon: Icons.Material.Filled.Timelapse,
            Action: TogglePomodoroModeAsync,
            Keywords: ["pomodoro", "modraw", "timer", "interval", "break", "virtual session", "mode"]));

        list.Add(new(
            Id: "action-yesterday-retro",
            Title: "Yesterday's dailies check-in",
            Subtitle: "Review and backdate unfinished dailies from yesterday",
            Category: "Actions",
            Icon: Icons.Material.Filled.History,
            Action: OpenYesterdayRetroAsync,
            Keywords: ["yesterday", "retro", "dailies", "checkin", "backdate", "review"]));

        list.Add(new(
            Id: "action-export-data",
            Title: "Export data",
            Subtitle: "Download a backup of your board and activity data",
            Category: "Actions",
            Icon: Icons.Material.Filled.FileDownload,
            Action: ExportDataAsync,
            Keywords: ["export", "backup", "download", "json", "data"]));

        list.Add(new(
            Id: "action-onboarding",
            Title: "Getting started guide",
            Subtitle: "View welcome tutorial and key shortcuts",
            Category: "Actions",
            Icon: Icons.Material.Filled.HelpOutline,
            Action: OpenOnboardingAsync,
            Keywords: ["guide", "help", "tutorial", "onboarding", "welcome", "shortcuts"]));

        // 4. Appearance and preferences
        list.Add(new(
            Id: "theme-dark",
            Title: "Dark theme",
            Subtitle: "Switch app appearance to dark mode",
            Category: "Appearance",
            Icon: Icons.Material.Filled.DarkMode,
            Action: () => SetThemeAsync(AppTheme.Dark),
            Keywords: ["theme", "dark", "mode", "color", "night"]));

        list.Add(new(
            Id: "theme-light",
            Title: "Light theme",
            Subtitle: "Switch app appearance to light mode",
            Category: "Appearance",
            Icon: Icons.Material.Filled.LightMode,
            Action: () => SetThemeAsync(AppTheme.Light),
            Keywords: ["theme", "light", "mode", "color", "day"]));

        list.Add(new(
            Id: "theme-system",
            Title: "System theme",
            Subtitle: "Sync app appearance with operating system",
            Category: "Appearance",
            Icon: Icons.Material.Filled.SettingsBrightness,
            Action: () => SetThemeAsync(AppTheme.System),
            Keywords: ["theme", "system", "auto", "os"]));

        list.Add(new(
            Id: "pref-toggle-shortcuts",
            Title: "Toggle keyboard shortcuts",
            Subtitle: "Enable or disable global hotkeys across the app",
            Category: "Appearance",
            Icon: Icons.Material.Filled.Keyboard,
            Action: ToggleKeyboardShortcutsAsync,
            Keywords: ["keyboard", "shortcuts", "hotkeys", "keys"]));

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

            // Direct delete intent detection: "delete <item>", "del <item>", "remove <item>"
            var isDeleteIntent = false;
            var searchTarget = q;
            if (q.StartsWith("delete ", StringComparison.OrdinalIgnoreCase) ||
                q.StartsWith("remove ", StringComparison.OrdinalIgnoreCase))
            {
                isDeleteIntent = true;
                searchTarget = q[7..].Trim();
            }
            else if (q.StartsWith("del ", StringComparison.OrdinalIgnoreCase))
            {
                isDeleteIntent = true;
                searchTarget = q[4..].Trim();
            }

            if (isDeleteIntent && !string.IsNullOrWhiteSpace(searchTarget))
            {
                // Direct delete commands for matching items
                foreach (var h in snapshot.Habits.Where(h => Matches(h.Title, h.Notes, h.Tags, searchTarget)).Take(6))
                {
                    results.Add(new CommandItem(
                        Id: $"direct-delete-habit-{h.Id}",
                        Title: $"Delete habit: {h.Title}",
                        Subtitle: "Permanently delete this habit",
                        Category: "Delete Items",
                        Icon: Icons.Material.Filled.Delete,
                        IsDanger: true,
                        Action: () => DeleteItemAsync(BoardSection.Habit, h.Id)));
                }

                foreach (var d in snapshot.Dailies.Where(d => Matches(d.Title, d.Notes, d.Tags, searchTarget)).Take(6))
                {
                    results.Add(new CommandItem(
                        Id: $"direct-delete-daily-{d.Id}",
                        Title: $"Delete daily: {d.Title}",
                        Subtitle: "Permanently delete this daily",
                        Category: "Delete Items",
                        Icon: Icons.Material.Filled.Delete,
                        IsDanger: true,
                        Action: () => DeleteItemAsync(BoardSection.Daily, d.Id)));
                }

                foreach (var t in snapshot.Todos.Where(t => Matches(t.Title, t.Notes, t.Tags, searchTarget)).Take(6))
                {
                    results.Add(new CommandItem(
                        Id: $"direct-delete-todo-{t.Id}",
                        Title: $"Delete to-do: {t.Title}",
                        Subtitle: "Permanently delete this to-do",
                        Category: "Delete Items",
                        Icon: Icons.Material.Filled.Delete,
                        IsDanger: true,
                        Action: () => DeleteItemAsync(BoardSection.Todo, t.Id)));
                }

                return results;
            }

            // Direct timer controls: resume, pause, stop, log, and reset
            if (q.Equals("resume", StringComparison.OrdinalIgnoreCase) && !_timer.IsRunning &&
                (_timer.Elapsed > TimeSpan.Zero || (_timer.PomodoroModeEnabled && _timer.CurrentPomodoroState != PomodoroState.Idle)))
            {
                var resumeLabel = _timer.PomodoroModeEnabled
                    ? $"Resume Pomodoro, {_timer.GetDisplayTime()} remaining"
                    : $"Resume focus session, {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)} elapsed";
                results.Add(new CommandItem(
                    Id: "timer-resume-search",
                    Title: "Resume focus session",
                    Subtitle: resumeLabel,
                    Category: "Timer",
                    Icon: Icons.Material.Filled.PlayArrow,
                    Action: () =>
                    {
                        Close();
                        _timer.Start();
                        return Task.CompletedTask;
                    }));
            }

            if (q.Equals("pause", StringComparison.OrdinalIgnoreCase) && _timer.IsRunning)
            {
                results.Add(new CommandItem(
                    Id: "timer-pause-search",
                    Title: "Pause focus timer",
                    Subtitle: _timer.PomodoroModeEnabled ? $"Pause active {_timer.StatusLabel}" : "Temporarily pause the running session",
                    Category: "Timer",
                    Icon: Icons.Material.Filled.Pause,
                    Action: () =>
                    {
                        Close();
                        _timer.Pause();
                        return Task.CompletedTask;
                    }));
            }

            if ((q.Equals("stop", StringComparison.OrdinalIgnoreCase) ||
                 q.Equals("log", StringComparison.OrdinalIgnoreCase) ||
                 q.Equals("lock", StringComparison.OrdinalIgnoreCase)) &&
                (_timer.IsRunning || _timer.Elapsed > TimeSpan.Zero || (_timer.PomodoroModeEnabled && _timer.CurrentPomodoroState != PomodoroState.Idle)))
            {
                results.Add(new CommandItem(
                    Id: "timer-stop-search",
                    Title: "Stop and log focus session",
                    Subtitle: $"Stop and log session of {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)} to target",
                    Category: "Timer",
                    Icon: Icons.Material.Filled.Stop,
                    Action: StopTimerSessionAsync));
            }

            if (q.Equals("reset", StringComparison.OrdinalIgnoreCase) &&
                (_timer.IsRunning || _timer.Elapsed > TimeSpan.Zero || (_timer.PomodoroModeEnabled && _timer.CurrentPomodoroState != PomodoroState.Idle)))
            {
                results.Add(new CommandItem(
                    Id: "timer-reset-search",
                    Title: "Reset focus timer",
                    Subtitle: _timer.PomodoroModeEnabled ? "Reset Pomodoro intervals and return to idle" : "Reset session timer to zero",
                    Category: "Timer",
                    Icon: Icons.Material.Filled.Refresh,
                    Action: async () =>
                    {
                        Close();
                        _timer.ResetSession();
                        await _notifier.NotifyAsync(_timer.PomodoroModeEnabled ? "Pomodoro session reset to idle." : "Stopwatch timer reset to zero.", Severity.Info, CancellationToken.None);
                    }));
            }

            // Direct Pomodoro quick-trigger
            if (q.Equals("pomodoro", StringComparison.OrdinalIgnoreCase) ||
                q.Equals("comodoro", StringComparison.OrdinalIgnoreCase) ||
                q.Equals("modraw", StringComparison.OrdinalIgnoreCase) ||
                q.Equals("pomo", StringComparison.OrdinalIgnoreCase))
            {
                var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
                results.Add(new CommandItem(
                    Id: "quick-start-pomodoro-search",
                    Title: "Start Pomodoro session",
                    Subtitle: $"Start {_timer.WorkDuration.TotalMinutes:0}m work interval for {targetName}",
                    Category: "Timer",
                    Icon: Icons.Material.Filled.Timelapse,
                    Action: StartPomodoroSessionAsync));
            }

            // Direct duration intent like "timer 25", "focus 45m", "session 30", or "start 25"
            var durationMatch = System.Text.RegularExpressions.Regex.Match(
                q,
                @"^(?:timer|focus|start|session)\s+(\d{1,3})(?:m|min)?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(250));
            if (durationMatch.Success && int.TryParse(durationMatch.Groups[1].Value, out var parsedMinutes) && parsedMinutes > 0)
            {
                var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
                results.Add(new CommandItem(
                    Id: $"quick-start-duration-{parsedMinutes}",
                    Title: $"Start {parsedMinutes}-minute focus session",
                    Subtitle: $"Start focus timer immediately with alert after {parsedMinutes} minutes for '{targetName}'",
                    Category: "Timer",
                    Icon: Icons.Material.Filled.PlayArrow,
                    Action: async () =>
                    {
                        Close();
                        _timer.PomodoroModeEnabled = false;
                        _timer.FocusAlertAfter = TimeSpan.FromMinutes(parsedMinutes);
                        _timer.Start();
                        await _notifier.NotifyAsync($"Started {parsedMinutes}-minute focus session for '{targetName}'.", Severity.Info, CancellationToken.None);
                    }));

                results.Add(new CommandItem(
                    Id: $"quick-set-alert-{parsedMinutes}",
                    Title: $"Set alert milestone to {parsedMinutes} minutes",
                    Subtitle: "Configure time's up alert milestone without starting timer",
                    Category: "Timer",
                    Icon: Icons.Material.Filled.HourglassTop,
                    Action: async () =>
                    {
                        Close();
                        _timer.PomodoroModeEnabled = false;
                        _timer.FocusAlertAfter = TimeSpan.FromMinutes(parsedMinutes);
                        await _notifier.NotifyAsync($"Focus duration set to {parsedMinutes} minutes.", Severity.Info, CancellationToken.None);
                    }));
            }

            // Normal search: Habits
            var matchingHabits = snapshot.Habits
                .Where(h => Matches(h.Title, h.Notes, h.Tags, q))
                .Take(6);

            foreach (var h in matchingHabits)
            {
                results.Add(new CommandItem(
                    Id: $"habit-{h.Id}",
                    Title: h.Title,
                    Subtitle: $"Habit, +{h.Counter}, -{h.NegativeCounter}",
                    Category: "Habits",
                    Icon: Icons.Material.Filled.Repeat,
                    ChildrenProvider: () => Task.FromResult(GetHabitSubActions(h))));
            }

            // Dailies
            var matchingDailies = snapshot.Dailies
                .Where(d => Matches(d.Title, d.Notes, d.Tags, q))
                .Take(6);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var d in matchingDailies)
            {
                var isDone = d.DailyLastCompletedOn.HasValue &&
                             d.DailyLastCompletedOn.Value == today;
                results.Add(new CommandItem(
                    Id: $"daily-{d.Id}",
                    Title: d.Title,
                    Subtitle: isDone ? $"Daily, completed today, streak: {d.Counter}" : $"Daily, streak: {d.Counter}",
                    Category: "Dailies",
                    Icon: isDone ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CalendarToday,
                    ChildrenProvider: () => Task.FromResult(GetDailySubActions(d, isDone))));
            }

            // To-dos
            var matchingTodos = snapshot.Todos
                .Where(t => Matches(t.Title, t.Notes, t.Tags, q))
                .Take(6);

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
                    Subtitle: $"To-do, {status}",
                    Category: "To-dos",
                    Icon: t.IsCompleted ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CheckBoxOutlineBlank,
                    ChildrenProvider: () => Task.FromResult(GetTodoSubActions(t))));
            }
        }
        catch
        {
            // Best effort search.
        }

        return results;
    }

    private List<CommandItem> GetHabitSubActions(BoardItem h) =>
    [
        new(
            Id: $"habit-{h.Id}-plus",
            Title: "+1 Increment count",
            Subtitle: "Log positive completion",
            Category: "Actions",
            Icon: Icons.Material.Filled.Add,
            Action: async () =>
            {
                Close();
                await _boardData.IncrementHabitPlusAsync(h.Id);
                await NotifyBoardRefreshAsync();
            },
            Keywords: ["plus", "increment", "+1", "add"]),
        new(
            Id: $"habit-{h.Id}-minus",
            Title: "-1 Decrement count",
            Subtitle: "Log setback count",
            Category: "Actions",
            Icon: Icons.Material.Filled.Remove,
            Action: async () =>
            {
                Close();
                await _boardData.IncrementHabitMinusAsync(h.Id);
                await NotifyBoardRefreshAsync();
            },
            Keywords: ["minus", "decrement", "-1", "subtract"]),
        new(
            Id: $"habit-{h.Id}-reset",
            Title: "Reset counters",
            Subtitle: "Set positive and negative counters back to 0",
            Category: "Actions",
            Icon: Icons.Material.Filled.RestartAlt,
            Action: async () =>
            {
                Close();
                await _boardData.UpdateHabitAsync(h.Id, UpdateHabitArgs.From(h) with { Counter = 0, NegativeCounter = 0 });
                await NotifyBoardRefreshAsync();
                await _notifier.NotifyAsync($"Counters for '{h.Title}' reset to 0.", Severity.Info);
            },
            Keywords: ["reset", "clear", "zero"]),
        new(
            Id: $"habit-{h.Id}-timer",
            Title: "Start focus timer on this habit",
            Subtitle: "Set timer target and start stopwatch",
            Category: "Actions",
            Icon: Icons.Material.Filled.Timer,
            Action: () =>
            {
                Close();
                _timer.SelectTarget("Habit", h.Title, h.Id);
                _timer.Start();
                return Task.CompletedTask;
            },
            Keywords: ["timer", "start", "focus", "stopwatch"]),
        new(
            Id: $"habit-{h.Id}-edit",
            Title: "Edit habit details",
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
            },
            Keywords: ["edit", "rename", "change", "modify", "notes", "checklist"]),
        new(
            Id: $"habit-{h.Id}-archive",
            Title: "Archive habit",
            Subtitle: "Move item to archive",
            Category: "Actions",
            Icon: Icons.Material.Filled.Archive,
            Action: async () =>
            {
                Close();
                await _boardData.ArchiveItemAsync(BoardSection.Habit, h.Id);
                await NotifyBoardRefreshAsync();
            },
            Keywords: ["archive", "hide"]),
        new(
            Id: $"habit-{h.Id}-delete",
            Title: "Delete habit",
            Subtitle: "Permanently delete this habit",
            Category: "Actions",
            Icon: Icons.Material.Filled.Delete,
            IsDanger: true,
            Action: () => DeleteItemAsync(BoardSection.Habit, h.Id),
            Keywords: ["delete", "remove", "trash", "destroy"])
    ];

    private List<CommandItem> GetDailySubActions(BoardItem d, bool isDone)
    {
        var actions = new List<CommandItem>
        {
            new(
                Id: $"daily-{d.Id}-toggle",
                Title: isDone ? "Unmark completed" : "Mark completed today",
                Subtitle: isDone ? "Reopen this daily for today" : "Record daily streak progression",
                Category: "Actions",
                Icon: isDone ? Icons.Material.Filled.RadioButtonUnchecked : Icons.Material.Filled.CheckCircle,
                Action: async () =>
                {
                    Close();
                    await _boardData.ToggleItemAsync(BoardSection.Daily, d.Id);
                    await NotifyBoardRefreshAsync();
                },
                Keywords: ["toggle", "done", "complete", "check"]),
            new(
                Id: $"daily-{d.Id}-yesterday",
                Title: "Mark completed for yesterday",
                Subtitle: "Backdate completion to yesterday",
                Category: "Actions",
                Icon: Icons.Material.Filled.History,
                Action: async () =>
                {
                    Close();
                    var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
                    await _boardData.CompleteDailyForDateAsync(d.Id, yesterday);
                    await NotifyBoardRefreshAsync();
                    await _notifier.NotifyAsync($"Marked '{d.Title}' completed for yesterday.", Severity.Success);
                },
                Keywords: ["yesterday", "retro", "backdate", "complete"]),
            new(
                Id: $"daily-{d.Id}-heatmap",
                Title: "View completion heatmap",
                Subtitle: "Show historical completions and streak calendar",
                Category: "Actions",
                Icon: Icons.Material.Filled.CalendarMonth,
                Action: async () =>
                {
                    Close();
                    var parameters = new DialogParameters<DailyHeatmapDialog>
                    {
                        { x => x.BoardItemId, d.Id },
                        { x => x.Title, d.Title }
                    };
                    await _dialogs.ShowAsync<DailyHeatmapDialog>(string.Empty, parameters, DialogDefaults.Wide);
                },
                Keywords: ["heatmap", "history", "streak", "calendar", "stats"]),
            new(
                Id: $"daily-{d.Id}-timer",
                Title: "Start focus timer on this daily",
                Subtitle: "Set timer target and start stopwatch",
                Category: "Actions",
                Icon: Icons.Material.Filled.Timer,
                Action: () =>
                {
                    Close();
                    _timer.SelectTarget("Daily", d.Title, d.Id);
                    _timer.Start();
                    return Task.CompletedTask;
                },
                Keywords: ["timer", "start", "focus", "stopwatch"]),
            new(
                Id: $"daily-{d.Id}-edit",
                Title: "Edit daily details",
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
                },
                Keywords: ["edit", "rename", "change", "schedule", "repeat", "notes", "checklist"]),
            new(
                Id: $"daily-{d.Id}-archive",
                Title: "Archive daily",
                Subtitle: "Move item to archive",
                Category: "Actions",
                Icon: Icons.Material.Filled.Archive,
                Action: async () =>
                {
                    Close();
                    await _boardData.ArchiveItemAsync(BoardSection.Daily, d.Id);
                    await NotifyBoardRefreshAsync();
                },
                Keywords: ["archive", "hide"]),
            new(
                Id: $"daily-{d.Id}-delete",
                Title: "Delete daily",
                Subtitle: "Permanently delete this daily",
                Category: "Actions",
                Icon: Icons.Material.Filled.Delete,
                IsDanger: true,
                Action: () => DeleteItemAsync(BoardSection.Daily, d.Id),
                Keywords: ["delete", "remove", "trash", "destroy"])
        };

        return actions;
    }

    private List<CommandItem> GetTodoSubActions(BoardItem t)
    {
        var actions = new List<CommandItem>
        {
            new(
                Id: $"todo-{t.Id}-toggle",
                Title: t.IsCompleted ? "Mark as incomplete" : "Mark as done",
                Subtitle: t.IsCompleted ? "Reopen this to-do" : "Complete task",
                Category: "Actions",
                Icon: t.IsCompleted ? Icons.Material.Filled.RadioButtonUnchecked : Icons.Material.Filled.CheckCircle,
                Action: async () =>
                {
                    Close();
                    await _boardData.ToggleItemAsync(BoardSection.Todo, t.Id);
                    await NotifyBoardRefreshAsync();
                },
                Keywords: ["toggle", "done", "complete", "finish", "reopen"]),
            new(
                Id: $"todo-{t.Id}-due-today",
                Title: "Set due date to today",
                Subtitle: "Set deadline to today",
                Category: "Actions",
                Icon: Icons.Material.Filled.Today,
                Action: async () =>
                {
                    Close();
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    await _boardData.UpdateTodoAsync(t.Id, UpdateTodoArgs.From(t) with { DueDate = today });
                    await NotifyBoardRefreshAsync();
                    await _notifier.NotifyAsync($"Due date for '{t.Title}' set to today.", Severity.Success);
                },
                Keywords: ["due", "today", "deadline", "date"]),
            new(
                Id: $"todo-{t.Id}-due-tomorrow",
                Title: "Set due date to tomorrow",
                Subtitle: "Set deadline to tomorrow",
                Category: "Actions",
                Icon: Icons.Material.Filled.Event,
                Action: async () =>
                {
                    Close();
                    var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
                    await _boardData.UpdateTodoAsync(t.Id, UpdateTodoArgs.From(t) with { DueDate = tomorrow });
                    await NotifyBoardRefreshAsync();
                    await _notifier.NotifyAsync($"Due date for '{t.Title}' set to tomorrow.", Severity.Success);
                },
                Keywords: ["due", "tomorrow", "deadline", "date"])
        };

        if (t.TodoDueDate.HasValue)
        {
            actions.Add(new(
                Id: $"todo-{t.Id}-clear-due",
                Title: "Clear due date",
                Subtitle: $"Remove deadline, currently {t.TodoDueDate.Value:MMM d}",
                Category: "Actions",
                Icon: Icons.Material.Filled.EventBusy,
                Action: async () =>
                {
                    Close();
                    await _boardData.UpdateTodoAsync(t.Id, UpdateTodoArgs.From(t) with { DueDate = null });
                    await NotifyBoardRefreshAsync();
                    await _notifier.NotifyAsync($"Cleared due date for '{t.Title}'.", Severity.Info);
                },
                Keywords: ["clear due", "remove deadline", "no date"]));
        }

        actions.Add(new(
            Id: $"todo-{t.Id}-timer",
            Title: "Start focus timer on this to-do",
            Subtitle: "Set timer target and start stopwatch",
            Category: "Actions",
            Icon: Icons.Material.Filled.Timer,
            Action: () =>
            {
                Close();
                _timer.SelectTarget("Todo", t.Title, t.Id);
                _timer.Start();
                return Task.CompletedTask;
            },
            Keywords: ["timer", "start", "focus", "stopwatch"]));

        actions.Add(new(
            Id: $"todo-{t.Id}-edit",
            Title: "Edit to-do details",
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
            },
            Keywords: ["edit", "rename", "change", "modify", "notes", "checklist"]));

        actions.Add(new(
            Id: $"todo-{t.Id}-archive",
            Title: "Archive to-do",
            Subtitle: "Move item to archive",
            Category: "Actions",
            Icon: Icons.Material.Filled.Archive,
            Action: async () =>
            {
                Close();
                await _boardData.ArchiveItemAsync(BoardSection.Todo, t.Id);
                await NotifyBoardRefreshAsync();
            },
            Keywords: ["archive", "hide"]));

        actions.Add(new(
            Id: $"todo-{t.Id}-delete",
            Title: "Delete to-do",
            Subtitle: "Permanently delete this to-do",
            Category: "Actions",
            Icon: Icons.Material.Filled.Delete,
            IsDanger: true,
            Action: () => DeleteItemAsync(BoardSection.Todo, t.Id),
            Keywords: ["delete", "remove", "trash", "destroy"]));

        return actions;
    }

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

    public async Task DeleteItemAsync(BoardSection section, Guid itemId)
    {
        Close();
        try
        {
            await _boardData.DeleteItemAsync(section, itemId);
            await NotifyBoardRefreshAsync();
        }
        catch
        {
            await _notifier.NotifyAsync("Could not delete item.", Severity.Error);
        }
    }

    public async Task DeleteCompletedTodosAsync()
    {
        Close();
        try
        {
            var snapshot = await _boardData.GetSnapshotAsync();
            var completed = snapshot.Todos.Where(t => t.IsCompleted).ToList();
            if (completed.Count == 0)
            {
                await _notifier.NotifyAsync("No completed to-dos to delete.", Severity.Info);
                return;
            }

            var deleted = 0;
            using (_undo.BeginBatch($"Delete {completed.Count} done to-dos"))
            {
                foreach (var todo in completed)
                {
                    var success = await _boardData.DeleteItemAsync(BoardSection.Todo, todo.Id);
                    if (success)
                    {
                        deleted++;
                    }
                }
            }

            await NotifyBoardRefreshAsync();
            if (deleted < completed.Count)
            {
                var itemWord = completed.Count == 1 ? "to-do" : "to-dos";
                await _notifier.NotifyAsync($"Removed {deleted} of {completed.Count} {itemWord}. Some could not be deleted.", Severity.Warning);
            }
            else
            {
                var itemWord = deleted == 1 ? "to-do" : "to-dos";
                await _notifier.NotifyAsync($"Deleted {deleted} completed {itemWord}.", Severity.Success);
            }
        }
        catch
        {
            await _notifier.NotifyAsync("Could not delete completed to-dos.", Severity.Error);
        }
    }

    public async Task ArchiveCompletedTodosAsync()
    {
        Close();
        try
        {
            var snapshot = await _boardData.GetSnapshotAsync();
            var completed = snapshot.Todos.Where(t => t.IsCompleted).ToList();
            if (completed.Count == 0)
            {
                await _notifier.NotifyAsync("No completed to-dos to archive.", Severity.Info);
                return;
            }

            var archived = 0;
            using (_undo.BeginBatch($"Archive {completed.Count} done to-dos"))
            {
                foreach (var todo in completed)
                {
                    var result = await _boardData.ArchiveItemAsync(BoardSection.Todo, todo.Id);
                    if (result is not null)
                    {
                        archived++;
                    }
                }
            }

            await NotifyBoardRefreshAsync();
            if (archived < completed.Count)
            {
                var itemWord = completed.Count == 1 ? "to-do" : "to-dos";
                await _notifier.NotifyAsync($"Archived {archived} of {completed.Count} {itemWord}. Some could not be archived.", Severity.Warning);
            }
            else
            {
                var itemWord = archived == 1 ? "to-do" : "to-dos";
                await _notifier.NotifyAsync($"Archived {archived} completed {itemWord}.", Severity.Success);
            }
        }
        catch
        {
            await _notifier.NotifyAsync("Could not archive completed to-dos.", Severity.Error);
        }
    }

    public async Task ExportDataAsync()
    {
        Close();
        try
        {
            string json;
            DateTimeOffset exportedAt;
            if (_export is not null)
            {
                var data = await _export.ExportAsync();
                json = System.Text.Json.JsonSerializer.Serialize(data, JsonDefaults.Export);
                exportedAt = data.ExportedAtUtc;
            }
            else
            {
                var snapshot = await _boardData.GetSnapshotAsync();
                json = System.Text.Json.JsonSerializer.Serialize(snapshot, JsonDefaults.Export);
                exportedAt = DateTimeOffset.UtcNow;
            }

            var fileName = $"habitinator-export-{exportedAt:yyyyMMdd-HHmm}.json";
            var downloaded = false;
            if (_js is not null)
            {
                try
                {
                    await _js.InvokeVoidAsync("habitinatorLoadScript", "_content/App.Shared.RCL/js/boardUiState.js");
                    downloaded = await _js.InvokeAsync<bool>("habitinatorDownloadJson", fileName, json);
                }
                catch
                {
                    downloaded = false;
                }
            }

            if (downloaded)
            {
                await _notifier.NotifyAsync("Export downloaded.", Severity.Success);
            }
            else
            {
                await _dialogs.ShowAsync<ExportDataDialog>(
                    "Your data export",
                    new DialogParameters<ExportDataDialog> { { x => x.JsonText, json } },
                    DialogDefaults.Wide);
            }
        }
        catch (Exception ex)
        {
            await _notifier.NotifyAsync($"Export failed: {ex.Message}", Severity.Error);
        }
    }

    private async Task OpenYesterdayRetroAsync()
    {
        Close();
        try
        {
            var snapshot = await _boardData.GetSnapshotAsync();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var missed = DailySchedule.GetYesterdayUncompletedDailies(snapshot.Dailies, today);

            if (missed.Count == 0)
            {
                await _notifier.NotifyAsync("No uncompleted dailies found for yesterday.", Severity.Info);
                return;
            }

            var yesterdayDate = today.AddDays(-1);
            var parameters = new DialogParameters<DailyYesterdayRetroDialog>
            {
                { x => x.DueOn, yesterdayDate },
                { x => x.Items, missed }
            };
            var dialog = await _dialogs.ShowAsync<DailyYesterdayRetroDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
            await dialog.Result;
            await NotifyBoardRefreshAsync();
        }
        catch
        {
            await _notifier.NotifyAsync("Could not open yesterday's dailies check-in.", Severity.Error);
        }
    }

    private async Task OpenOnboardingAsync()
    {
        Close();
        await _dialogs.ShowAsync<OnboardingDialog>(string.Empty, DialogDefaults.SmallEditor);
    }

    public async Task TogglePomodoroModeAsync()
    {
        Close();
        _timer.TogglePomodoroMode();
        await _notifier.NotifyAsync(
            _timer.PomodoroModeEnabled ? "Pomodoro mode enabled." : "Pomodoro mode disabled. Stopwatch mode enabled.",
            Severity.Info);
    }

    public async Task StopTimerSessionAsync()
    {
        Close();
        var elapsed = _timer.Stop();
        _timer.ClearTargetAndFocus();
        if (elapsed > TimeSpan.Zero)
        {
            if (_timerSessionLog is not null)
            {
                var result = await _timerSessionLog.LogStoppedSessionAsync(elapsed);
                if (result.BoardUpdateFailed || !result.BoardProgressed)
                {
                    await _notifier.NotifyAsync(result.UserMessage, result.BoardUpdateFailed ? Severity.Error : Severity.Success);
                }
            }
            else
            {
                await _notifier.NotifyAsync($"Focus session stopped. Logged {GlobalTimerService.FormatTimeSpan(elapsed)}.", Severity.Success);
            }
            await NotifyBoardRefreshAsync();
        }
        else
        {
            await _notifier.NotifyAsync("Focus session stopped.", Severity.Info);
        }
    }

    public async Task StartPomodoroSessionAsync()
    {
        Close();
        _timer.PomodoroModeEnabled = true;
        if (!_timer.IsRunning)
        {
            _timer.Start();
        }
        var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
        await _notifier.NotifyAsync($"Pomodoro session started for '{targetName}'.", Severity.Info);
    }

    private async Task PromptCustomDurationAndStartAsync(string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel)
    {
        Close();
        var parameters = new DialogParameters<SetFocusDurationDialog>
        {
            { x => x.TargetTitle, displayLabel },
            { x => x.InitialDuration, _timer.FocusAlertAfter },
            { x => x.StartSession, true }
        };
        var dialog = await _dialogs.ShowAsync<SetFocusDurationDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
        {
            var duration = result.Data as TimeSpan?;
            if (targetType is not null && targetTitle is not null)
            {
                _timer.SelectTarget(targetType, targetTitle, boardItemId);
            }
            else if (displayLabel != "General Session")
            {
                _timer.SetManualTarget(displayLabel);
            }
            else
            {
                _timer.SetManualTarget(null);
            }
            _timer.PomodoroModeEnabled = false;
            _timer.FocusAlertAfter = duration;
            _timer.Start();
            var durLabel = duration.HasValue ? GlobalTimerService.FormatTimeSpan(duration.Value) : "continuous";
            await _notifier.NotifyAsync($"Started {durLabel} focus session for '{displayLabel}'.", Severity.Info);
        }
    }

    private async Task PromptCustomPomodoroDurationAndStartAsync(string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel)
    {
        Close();
        var parameters = new DialogParameters<SetFocusDurationDialog>
        {
            { x => x.TargetTitle, $"{displayLabel}, Pomodoro" },
            { x => x.InitialDuration, _timer.WorkDuration },
            { x => x.StartSession, true }
        };
        var dialog = await _dialogs.ShowAsync<SetFocusDurationDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
        {
            var duration = result.Data as TimeSpan?;
            _timer.PomodoroModeEnabled = true;
            if (duration.HasValue && duration.Value > TimeSpan.Zero)
            {
                _timer.WorkDuration = duration.Value;
                _timer.FocusAlertAfter = duration.Value;
            }
            if (targetType is not null && targetTitle is not null)
            {
                _timer.SelectTarget(targetType, targetTitle, boardItemId);
            }
            else if (displayLabel != "General Session")
            {
                _timer.SetManualTarget(displayLabel);
            }
            else
            {
                _timer.SetManualTarget(null);
            }
            _timer.Start();
            var durLabel = duration.HasValue ? GlobalTimerService.FormatTimeSpan(duration.Value) : GlobalTimerService.FormatTimeSpan(_timer.WorkDuration);
            await _notifier.NotifyAsync($"Pomodoro session of {durLabel} work started for '{displayLabel}'.", Severity.Info);
        }
    }

    private async Task PromptCustomTargetAndDurationFlowAsync(bool isPomodoro)
    {
        Close();
        var parameters = new DialogParameters<SetSessionTargetDialog>
        {
            { x => x.CurrentTarget, _timer.TargetId }
        };
        var dialog = await _dialogs.ShowAsync<SetSessionTargetDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled && result.Data is string customTarget && !string.IsNullOrWhiteSpace(customTarget))
        {
            var trimmed = customTarget.Trim();
            if (isPomodoro)
            {
                await PromptCustomPomodoroDurationAndStartAsync(null, null, null, trimmed);
            }
            else
            {
                await PromptCustomDurationAndStartAsync(null, null, null, trimmed);
            }
        }
    }

    private async Task PromptSetCustomTargetAsync()
    {
        Close();
        var parameters = new DialogParameters<SetSessionTargetDialog>
        {
            { x => x.CurrentTarget, _timer.TargetId }
        };
        var dialog = await _dialogs.ShowAsync<SetSessionTargetDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled && result.Data is string customTarget)
        {
            var trimmed = customTarget.Trim();
            _timer.SetManualTarget(string.IsNullOrWhiteSpace(trimmed) ? null : trimmed);
            var label = string.IsNullOrWhiteSpace(trimmed) ? "Cleared session target." : $"Session target set to '{trimmed}'.";
            await _notifier.NotifyAsync(label, Severity.Info);
        }
    }

    private async Task PromptSetCustomDurationAsync()
    {
        Close();
        var parameters = new DialogParameters<SetFocusDurationDialog>
        {
            { x => x.TargetTitle, _timer.TargetId },
            { x => x.InitialDuration, _timer.FocusAlertAfter },
            { x => x.StartSession, false }
        };
        var dialog = await _dialogs.ShowAsync<SetFocusDurationDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
        {
            var duration = result.Data as TimeSpan?;
            _timer.PomodoroModeEnabled = false;
            _timer.FocusAlertAfter = duration;
            var label = duration.HasValue ? GlobalTimerService.FormatTimeSpan(duration.Value) : "continuous";
            await _notifier.NotifyAsync($"Focus duration milestone set to {label}.", Severity.Info);
        }
    }

    private Task<List<CommandItem>> GetTimerModeSelectionCommandsAsync()
    {
        return Task.FromResult<List<CommandItem>>([
            new(
                Id: "mode-stopwatch",
                Title: !_timer.PomodoroModeEnabled ? "Stopwatch session, active mode" : "Stopwatch session",
                Subtitle: !_timer.PomodoroModeEnabled
                    ? "Currently active mode. Open count-up focus with optional duration milestone"
                    : "Open count-up focus with optional duration milestone",
                Category: "Mode",
                Icon: !_timer.PomodoroModeEnabled ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                ChildrenProvider: () => GetTargetsForModeAsync(isPomodoro: false),
                Keywords: ["stopwatch", "timer", "open", "continuous", "count-up"]),

            new(
                Id: "mode-pomodoro",
                Title: _timer.PomodoroModeEnabled ? "Pomodoro session, active mode" : "Pomodoro session",
                Subtitle: _timer.PomodoroModeEnabled
                    ? "Currently active mode. Interval focus with scheduled work and break periods"
                    : "Interval focus with scheduled work and break periods",
                Category: "Mode",
                Icon: _timer.PomodoroModeEnabled ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timelapse,
                ChildrenProvider: () => GetTargetsForModeAsync(isPomodoro: true),
                Keywords: ["pomodoro", "comodoro", "modraw", "interval", "work", "break"])
        ]);
    }

    private async Task<List<CommandItem>> GetTargetsForModeAsync(bool isPomodoro)
    {
        var modeName = isPomodoro ? "Pomodoro" : "Stopwatch";
        var targets = new List<CommandItem>
        {
            new(
                Id: "timer-target-empty",
                Title: "Untargeted session",
                Subtitle: $"Start {modeName} session without an item target",
                Category: "Session Target",
                Icon: Icons.Material.Outlined.HourglassEmpty,
                ChildrenProvider: () => Task.FromResult(GetDurationsForTargetAndMode(isPomodoro, null, null, null, "General Session")),
                Keywords: ["empty", "none", "general", "open", "free", "session"]),

            new(
                Id: "timer-target-custom",
                Title: "Custom target label...",
                Subtitle: $"Type a custom session label and choose duration for {modeName}",
                Category: "Session Target",
                Icon: Icons.Material.Filled.Edit,
                Action: () => PromptCustomTargetAndDurationFlowAsync(isPomodoro),
                Keywords: ["custom", "target", "label", "text", "manual"])
        };

        try
        {
            var snapshot = await _boardData.GetSnapshotAsync();

            foreach (var h in snapshot.Habits)
            {
                targets.Add(new CommandItem(
                    Id: $"timer-target-habit-{h.Id}",
                    Title: h.Title,
                    Subtitle: $"Habit, +{h.Counter}, -{h.NegativeCounter}",
                    Category: "Habits",
                    Icon: Icons.Material.Filled.Repeat,
                    ChildrenProvider: () => Task.FromResult(GetDurationsForTargetAndMode(isPomodoro, "Habit", h.Title, h.Id, h.Title)),
                    Keywords: ["habit", h.Title]));
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var d in snapshot.Dailies)
            {
                var isDone = d.DailyLastCompletedOn.HasValue && d.DailyLastCompletedOn.Value == today;
                var dailySub = isDone ? $"Daily, completed today, streak: {d.Counter}" : $"Daily, streak: {d.Counter}";
                targets.Add(new CommandItem(
                    Id: $"timer-target-daily-{d.Id}",
                    Title: d.Title,
                    Subtitle: dailySub,
                    Category: "Dailies",
                    Icon: isDone ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CalendarToday,
                    ChildrenProvider: () => Task.FromResult(GetDurationsForTargetAndMode(isPomodoro, "Daily", d.Title, d.Id, d.Title)),
                    Keywords: ["daily", d.Title]));
            }

            foreach (var t in snapshot.Todos.Where(todo => !todo.IsCompleted))
            {
                var due = t.TodoDueDate.HasValue ? $"Due {t.TodoDueDate.Value:MMM d}" : "Open";
                targets.Add(new CommandItem(
                    Id: $"timer-target-todo-{t.Id}",
                    Title: t.Title,
                    Subtitle: $"To-do, {due}",
                    Category: "To-dos",
                    Icon: Icons.Material.Filled.CheckBoxOutlineBlank,
                    ChildrenProvider: () => Task.FromResult(GetDurationsForTargetAndMode(isPomodoro, "Todo", t.Title, t.Id, t.Title)),
                    Keywords: ["todo", t.Title]));
            }
        }
        catch
        {
            // Best effort.
        }

        return targets;
    }

    private List<CommandItem> GetDurationsForTargetAndMode(bool isPomodoro, string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel)
    {
        var targetSlug = targetTitle is not null ? targetTitle.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant() : "empty";

        if (isPomodoro)
        {
            return
            [
                new(
                    Id: $"start-{targetSlug}-custom",
                    Title: "Custom work duration and start...",
                    Subtitle: "Enter custom minutes like 35m and start immediately",
                    Category: "Duration",
                    Icon: Icons.Material.Filled.EditCalendar,
                    Action: () => PromptCustomPomodoroDurationAndStartAsync(targetType, targetTitle, boardItemId, displayLabel),
                    Keywords: ["custom", "duration", "pomodoro", "minutes"]),

                new(
                    Id: $"start-{targetSlug}-pomodoro",
                    Title: $"Default interval, {_timer.WorkDuration.TotalMinutes:0}m work",
                    Subtitle: isPomodoro
                        ? $"Currently configured. Start standard {GlobalTimerService.FormatTimeSpan(_timer.WorkDuration)} work session"
                        : "Start standard Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.PlayArrow,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, _timer.WorkDuration),
                    Keywords: ["pomodoro", "default", "standard"]),

                new(
                    Id: $"start-{targetSlug}-pomo-15m",
                    Title: "15-minute work interval",
                    Subtitle: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(15)
                        ? "Currently configured. Start 15-minute Pomodoro work session"
                        : "Start 15-minute Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(15)
                        ? Icons.Material.Filled.CheckCircle
                        : Icons.Material.Filled.Timer,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(15)),
                    Keywords: ["15", "15m"]),

                new(
                    Id: $"start-{targetSlug}-pomo-20m",
                    Title: "20-minute work interval",
                    Subtitle: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(20)
                        ? "Currently configured. Start 20-minute Pomodoro work session"
                        : "Start 20-minute Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(20)
                        ? Icons.Material.Filled.CheckCircle
                        : Icons.Material.Filled.Timer,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(20)),
                    Keywords: ["20", "20m"]),

                new(
                    Id: $"start-{targetSlug}-pomo-25m",
                    Title: "25-minute work interval",
                    Subtitle: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(25)
                        ? "Currently configured. Start 25-minute Pomodoro work session"
                        : "Start 25-minute Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(25)
                        ? Icons.Material.Filled.CheckCircle
                        : Icons.Material.Filled.Timer,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(25)),
                    Keywords: ["25", "25m"]),

                new(
                    Id: $"start-{targetSlug}-pomo-30m",
                    Title: "30-minute work interval",
                    Subtitle: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(30)
                        ? "Currently configured. Start 30-minute Pomodoro work session"
                        : "Start 30-minute Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(30)
                        ? Icons.Material.Filled.CheckCircle
                        : Icons.Material.Filled.Timer,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(30)),
                    Keywords: ["30", "30m"]),

                new(
                    Id: $"start-{targetSlug}-pomo-45m",
                    Title: "45-minute work interval",
                    Subtitle: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(45)
                        ? "Currently configured. Start 45-minute Pomodoro work session"
                        : "Start 45-minute Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(45)
                        ? Icons.Material.Filled.CheckCircle
                        : Icons.Material.Filled.Timer,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(45)),
                    Keywords: ["45", "45m"]),

                new(
                    Id: $"start-{targetSlug}-pomo-50m",
                    Title: "50-minute work interval",
                    Subtitle: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(50)
                        ? "Currently configured. Start 50-minute Pomodoro work session"
                        : "Start 50-minute Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(50)
                        ? Icons.Material.Filled.CheckCircle
                        : Icons.Material.Filled.Timer,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(50)),
                    Keywords: ["50", "50m"])
            ];
        }

        return
        [
            new(
                Id: $"start-{targetSlug}-custom",
                Title: "Custom duration and start...",
                Subtitle: "Enter custom minutes or mm:ss and start immediately",
                Category: "Duration",
                Icon: Icons.Material.Filled.EditCalendar,
                Action: () => PromptCustomDurationAndStartAsync(targetType, targetTitle, boardItemId, displayLabel),
                Keywords: ["custom", "duration", "time", "minutes"]),

            new(
                Id: $"start-{targetSlug}-open",
                Title: "Continuous count-up without limit",
                Subtitle: !isPomodoro && _timer.FocusAlertAfter is null
                    ? "Currently selected. Run stopwatch with no time alert"
                    : "Run stopwatch with no time alert",
                Category: "Duration",
                Icon: !isPomodoro && _timer.FocusAlertAfter is null
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.AllInclusive,
                Action: async () =>
                {
                    Close();
                    _timer.PomodoroModeEnabled = false;
                    _timer.FocusAlertAfter = null;
                    if (targetType is not null && targetTitle is not null)
                    {
                        _timer.SelectTarget(targetType, targetTitle, boardItemId);
                    }
                    else if (displayLabel != "General Session")
                    {
                        _timer.SetManualTarget(displayLabel);
                    }
                    else
                    {
                        _timer.SetManualTarget(null);
                    }
                    _timer.Start();
                    await _notifier.NotifyAsync($"Started open stopwatch session for '{displayLabel}'.", Severity.Info);
                },
                Keywords: ["open", "continuous", "stopwatch", "unlimited"]),

            new(
                Id: $"start-{targetSlug}-15m",
                Title: "15 minutes",
                Subtitle: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(15)
                    ? "Currently selected. Start stopwatch with alert after 15 minutes"
                    : "Start stopwatch with alert after 15 minutes",
                Category: "Duration",
                Icon: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(15)
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.Timer,
                Action: () => StartStopwatchWithDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(15)),
                Keywords: ["15", "15m", "15 min", "quarter"]),

            new(
                Id: $"start-{targetSlug}-25m",
                Title: "25 minutes",
                Subtitle: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(25)
                    ? "Currently selected. Start stopwatch with alert after 25 minutes"
                    : "Start stopwatch with alert after 25 minutes",
                Category: "Duration",
                Icon: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(25)
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.Timer,
                Action: () => StartStopwatchWithDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(25)),
                Keywords: ["25", "25m", "25 min"]),

            new(
                Id: $"start-{targetSlug}-30m",
                Title: "30 minutes",
                Subtitle: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(30)
                    ? "Currently selected. Start stopwatch with alert after 30 minutes"
                    : "Start stopwatch with alert after 30 minutes",
                Category: "Duration",
                Icon: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(30)
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.Timer,
                Action: () => StartStopwatchWithDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(30)),
                Keywords: ["30", "30m", "30 min", "half hour"]),

            new(
                Id: $"start-{targetSlug}-45m",
                Title: "45 minutes",
                Subtitle: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(45)
                    ? "Currently selected. Start stopwatch with alert after 45 minutes"
                    : "Start stopwatch with alert after 45 minutes",
                Category: "Duration",
                Icon: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(45)
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.Timer,
                Action: () => StartStopwatchWithDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(45)),
                Keywords: ["45", "45m", "45 min"]),

            new(
                Id: $"start-{targetSlug}-60m",
                Title: "60 minutes, 1 hour",
                Subtitle: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(60)
                    ? "Currently selected. Start stopwatch with alert after 60 minutes"
                    : "Start stopwatch with alert after 60 minutes",
                Category: "Duration",
                Icon: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(60)
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.Timer,
                Action: () => StartStopwatchWithDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(60)),
                Keywords: ["60", "60m", "60 min", "hour", "1h"])
        ];
    }

    private async Task StartPomodoroWithWorkDurationAsync(string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel, TimeSpan workDuration)
    {
        Close();
        _timer.PomodoroModeEnabled = true;
        _timer.WorkDuration = workDuration;
        _timer.FocusAlertAfter = workDuration;
        if (targetType is not null && targetTitle is not null)
        {
            _timer.SelectTarget(targetType, targetTitle, boardItemId);
        }
        else if (displayLabel != "General Session")
        {
            _timer.SetManualTarget(displayLabel);
        }
        else
        {
            _timer.SetManualTarget(null);
        }
        _timer.Start();
        await _notifier.NotifyAsync($"Pomodoro work session of {GlobalTimerService.FormatTimeSpan(workDuration)} started for '{displayLabel}'.", Severity.Info);
    }

    private async Task StartStopwatchWithDurationAsync(string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel, TimeSpan duration)
    {
        Close();
        _timer.PomodoroModeEnabled = false;
        _timer.FocusAlertAfter = duration;
        if (targetType is not null && targetTitle is not null)
        {
            _timer.SelectTarget(targetType, targetTitle, boardItemId);
        }
        else if (displayLabel != "General Session")
        {
            _timer.SetManualTarget(displayLabel);
        }
        else
        {
            _timer.SetManualTarget(null);
        }
        _timer.Start();
        await _notifier.NotifyAsync($"Started {GlobalTimerService.FormatTimeSpan(duration)} focus session for '{displayLabel}'.", Severity.Info);
    }

    private async Task<List<CommandItem>> GetTimerTargetConfigurationCommandsAsync()
    {
        var items = new List<CommandItem>();
        try
        {
            if (_timer.TargetId is not null)
            {
                items.Add(new CommandItem(
                    Id: "target-config-clear",
                    Title: "Clear active target",
                    Subtitle: $"Remove current target '{_timer.TargetId}'",
                    Category: "Target",
                    Icon: Icons.Material.Filled.TimerOff,
                    Action: async () =>
                    {
                        Close();
                        _timer.SetManualTarget(null);
                        await _notifier.NotifyAsync("Timer target cleared.", Severity.Info);
                    },
                    Keywords: ["clear", "none", "empty", "remove"]));
            }

            items.Add(new CommandItem(
                Id: "target-config-custom",
                Title: "Custom target label...",
                Subtitle: "Type a custom free-text label for this session",
                Category: "Target",
                Icon: Icons.Material.Filled.Edit,
                Action: PromptSetCustomTargetAsync,
                Keywords: ["custom", "target", "label", "text", "manual"]));

            var snapshot = await _boardData.GetSnapshotAsync();

            foreach (var h in snapshot.Habits)
            {
                items.Add(new CommandItem(
                    Id: $"target-config-habit-{h.Id}",
                    Title: h.Title,
                    Subtitle: $"Habit, +{h.Counter}, -{h.NegativeCounter}",
                    Category: "Habits",
                    Icon: Icons.Material.Filled.Repeat,
                    Action: async () =>
                    {
                        Close();
                        _timer.SelectTarget("Habit", h.Title, h.Id);
                        await _notifier.NotifyAsync($"Session target set to habit '{h.Title}'.", Severity.Info);
                    },
                    Keywords: ["habit", h.Title]));
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var d in snapshot.Dailies)
            {
                var isDone = d.DailyLastCompletedOn.HasValue && d.DailyLastCompletedOn.Value == today;
                var dailySub = isDone ? $"Daily, completed today, streak: {d.Counter}" : $"Daily, streak: {d.Counter}";
                items.Add(new CommandItem(
                    Id: $"target-config-daily-{d.Id}",
                    Title: d.Title,
                    Subtitle: dailySub,
                    Category: "Dailies",
                    Icon: isDone ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CalendarToday,
                    Action: async () =>
                    {
                        Close();
                        _timer.SelectTarget("Daily", d.Title, d.Id);
                        await _notifier.NotifyAsync($"Session target set to daily '{d.Title}'.", Severity.Info);
                    },
                    Keywords: ["daily", d.Title]));
            }

            foreach (var t in snapshot.Todos.Where(todo => !todo.IsCompleted))
            {
                var due = t.TodoDueDate.HasValue ? $"Due {t.TodoDueDate.Value:MMM d}" : "Open";
                items.Add(new CommandItem(
                    Id: $"target-config-todo-{t.Id}",
                    Title: t.Title,
                    Subtitle: $"To-do, {due}",
                    Category: "To-dos",
                    Icon: Icons.Material.Filled.CheckBoxOutlineBlank,
                    Action: async () =>
                    {
                        Close();
                        _timer.SelectTarget("Todo", t.Title, t.Id);
                        await _notifier.NotifyAsync($"Session target set to to-do '{t.Title}'.", Severity.Info);
                    },
                    Keywords: ["todo", t.Title]));
            }
        }
        catch
        {
            // Best effort.
        }

        return items;
    }

    private Task<List<CommandItem>> GetDurationConfigurationCommandsAsync()
    {
        var targetDisplay = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
        var list = new List<CommandItem>
        {
            new(
                Id: "duration-config-custom",
                Title: "Custom duration and start...",
                Subtitle: $"Enter custom duration and immediately start session for '{targetDisplay}'",
                Category: "Duration",
                Icon: Icons.Material.Filled.EditCalendar,
                Action: () => PromptCustomDurationAndStartAsync(_timer.TargetType, _timer.TargetId, _timer.BoardItemId, targetDisplay),
                Keywords: ["custom", "duration", "start", "minutes"]),

            new(
                Id: "duration-config-custom-milestone",
                Title: "Set custom duration milestone...",
                Subtitle: "Set time's up alert milestone without starting timer",
                Category: "Duration",
                Icon: Icons.Material.Filled.HourglassTop,
                Action: PromptSetCustomDurationAsync,
                Keywords: ["custom", "duration", "alert", "milestone"]),

            new(
                Id: "duration-config-open",
                Title: "Continuous count-up without alert",
                Subtitle: _timer.FocusAlertAfter is null
                    ? "Currently selected. Stopwatch will run until manually paused or stopped"
                    : "Stopwatch will run until manually paused or stopped",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter is null ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.AllInclusive,
                Action: async () =>
                {
                    Close();
                    _timer.FocusAlertAfter = null;
                    await _notifier.NotifyAsync("Focus duration cleared for continuous session.", Severity.Info);
                },
                Keywords: ["open", "continuous", "clear", "stopwatch"]),

            new(
                Id: "duration-config-15m",
                Title: "15 minutes",
                Subtitle: _timer.FocusAlertAfter == TimeSpan.FromMinutes(15)
                    ? "Currently selected. Set time's up alert milestone to 15 minutes"
                    : "Set time's up alert milestone to 15 minutes",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter == TimeSpan.FromMinutes(15) ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                Action: async () =>
                {
                    Close();
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(15);
                    await _notifier.NotifyAsync("Focus duration set to 15 minutes.", Severity.Info);
                },
                Keywords: ["15", "15m", "quarter"]),

            new(
                Id: "duration-config-25m",
                Title: "25 minutes",
                Subtitle: _timer.FocusAlertAfter == TimeSpan.FromMinutes(25)
                    ? "Currently selected. Set time's up alert milestone to 25 minutes"
                    : "Set time's up alert milestone to 25 minutes",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter == TimeSpan.FromMinutes(25) ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                Action: async () =>
                {
                    Close();
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(25);
                    await _notifier.NotifyAsync("Focus duration set to 25 minutes.", Severity.Info);
                },
                Keywords: ["25", "25m"]),

            new(
                Id: "duration-config-30m",
                Title: "30 minutes",
                Subtitle: _timer.FocusAlertAfter == TimeSpan.FromMinutes(30)
                    ? "Currently selected. Set time's up alert milestone to 30 minutes"
                    : "Set time's up alert milestone to 30 minutes",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter == TimeSpan.FromMinutes(30) ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                Action: async () =>
                {
                    Close();
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(30);
                    await _notifier.NotifyAsync("Focus duration set to 30 minutes.", Severity.Info);
                },
                Keywords: ["30", "30m", "half hour"]),

            new(
                Id: "duration-config-45m",
                Title: "45 minutes",
                Subtitle: _timer.FocusAlertAfter == TimeSpan.FromMinutes(45)
                    ? "Currently selected. Set time's up alert milestone to 45 minutes"
                    : "Set time's up alert milestone to 45 minutes",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter == TimeSpan.FromMinutes(45) ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                Action: async () =>
                {
                    Close();
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(45);
                    await _notifier.NotifyAsync("Focus duration set to 45 minutes.", Severity.Info);
                },
                Keywords: ["45", "45m"]),

            new(
                Id: "duration-config-60m",
                Title: "60 minutes, 1 hour",
                Subtitle: _timer.FocusAlertAfter == TimeSpan.FromMinutes(60)
                    ? "Currently selected. Set time's up alert milestone to 60 minutes"
                    : "Set time's up alert milestone to 60 minutes",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter == TimeSpan.FromMinutes(60) ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                Action: async () =>
                {
                    Close();
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(60);
                    await _notifier.NotifyAsync("Focus duration set to 60 minutes.", Severity.Info);
                },
                Keywords: ["60", "60m", "hour", "1h"]),

            new(
                Id: "duration-config-90m",
                Title: "90 minutes, 1.5 hours",
                Subtitle: _timer.FocusAlertAfter == TimeSpan.FromMinutes(90)
                    ? "Currently selected. Set time's up alert milestone to 90 minutes"
                    : "Set time's up alert milestone to 90 minutes",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter == TimeSpan.FromMinutes(90) ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                Action: async () =>
                {
                    Close();
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(90);
                    await _notifier.NotifyAsync("Focus duration set to 90 minutes.", Severity.Info);
                },
                Keywords: ["90", "90m", "1.5h"])
        };

        if (_timer.PomodoroModeEnabled)
        {
            list.Insert(0, new(
                Id: "duration-pomodoro-info",
                Title: $"Pomodoro interval: {GlobalTimerService.FormatTimeSpan(_timer.WorkDuration)} work and {GlobalTimerService.FormatTimeSpan(_timer.ShortBreakDuration)} break",
                Subtitle: "Configured in Settings. Setting a custom duration above switches to Stopwatch mode.",
                Category: "Duration",
                Icon: Icons.Material.Filled.Settings,
                Action: () => NavigateAsync("/settings"),
                Keywords: ["pomodoro", "settings", "interval", "duration"]));
        }

        return Task.FromResult(list);
    }

    private async Task ToggleKeyboardShortcutsAsync()
    {
        Close();
        try
        {
            var prefs = await _preferences.GetAsync();
            prefs.EnableKeyboardShortcuts = !prefs.EnableKeyboardShortcuts;
            await _preferences.SaveAsync(prefs);
            await _notifier.NotifyAsync(
                prefs.EnableKeyboardShortcuts ? "Keyboard shortcuts enabled." : "Keyboard shortcuts disabled.",
                Severity.Info);
        }
        catch
        {
            // Best effort.
        }
    }

    private async Task<List<CommandItem>> GetManageItemsSubActionsAsync()
    {
        var items = new List<CommandItem>();
        try
        {
            var snapshot = await _boardData.GetSnapshotAsync();

            foreach (var h in snapshot.Habits)
            {
                items.Add(new CommandItem(
                    Id: $"manage-habit-{h.Id}",
                    Title: h.Title,
                    Subtitle: $"Habit, +{h.Counter}, -{h.NegativeCounter}",
                    Category: "Habits",
                    Icon: Icons.Material.Filled.Repeat,
                    ChildrenProvider: () => Task.FromResult(GetHabitSubActions(h))));
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var d in snapshot.Dailies)
            {
                var isDone = d.DailyLastCompletedOn.HasValue && d.DailyLastCompletedOn.Value == today;
                items.Add(new CommandItem(
                    Id: $"manage-daily-{d.Id}",
                    Title: d.Title,
                    Subtitle: isDone ? $"Daily, completed today, streak: {d.Counter}" : $"Daily, streak: {d.Counter}",
                    Category: "Dailies",
                    Icon: isDone ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CalendarToday,
                    ChildrenProvider: () => Task.FromResult(GetDailySubActions(d, isDone))));
            }

            foreach (var t in snapshot.Todos)
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

                items.Add(new CommandItem(
                    Id: $"manage-todo-{t.Id}",
                    Title: t.Title,
                    Subtitle: $"To-do, {status}",
                    Category: "To-dos",
                    Icon: t.IsCompleted ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CheckBoxOutlineBlank,
                    ChildrenProvider: () => Task.FromResult(GetTodoSubActions(t))));
            }
        }
        catch
        {
            // Best effort.
        }

        return items;
    }
}
