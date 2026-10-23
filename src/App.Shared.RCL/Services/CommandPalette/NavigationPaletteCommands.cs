using App.Shared.RCL.Components;
using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace App.Shared.RCL.Services.CommandPalette;

/// <summary>Navigation, dialogs, theme, and preference commands.</summary>
internal sealed class NavigationPaletteCommands(
    NavigationManager nav,
    IBoardDataService boardData,
    IDialogService dialogs,
    IUserPreferencesService preferences,
    IUserNotifier notifier,
    Action close,
    Func<Task> notifyRefresh,
    BoardUiSessionState? uiSession = null)
{
    private readonly NavigationManager _nav = nav;
    private readonly IBoardDataService _boardData = boardData;
    private readonly IDialogService _dialogs = dialogs;
    private readonly IUserPreferencesService _preferences = preferences;
    private readonly IUserNotifier _notifier = notifier;
    private readonly Action _close = close;
    private readonly Func<Task> _notifyRefresh = notifyRefresh;
    private readonly BoardUiSessionState? _uiSession = uiSession;

    private const string NavigationCategory = "Navigation";
    private const string AppearanceCategory = "Appearance";
    private const string SettingsPath = "/settings";
    private const string KwTheme = "theme";

    public void AddNavigationCommands(List<CommandItem> list)
    {
        list.Add(new(
            Id: "nav-board",
            Title: "Board",
            Subtitle: "Overview of habits, dailies, and to-dos",
            Category: NavigationCategory,
            Icon: Icons.Material.Filled.SpaceDashboard,
            ShortcutBadge: "G B",
            Action: () => NavigateAsync("/"),
            Keywords: ["board", "home", "main", "habits", "dailies", "todos"]));

        list.Add(new(
            Id: "nav-stats",
            Title: "Statistics",
            Subtitle: "Activity heatmap, history, and completions",
            Category: NavigationCategory,
            Icon: Icons.Material.Filled.BarChart,
            ShortcutBadge: "G S",
            Action: () => NavigateAsync("/stats"),
            Keywords: ["stats", "statistics", "charts", "history", "analytics"]));

        list.Add(new(
            Id: "nav-upcoming",
            Title: "Upcoming",
            Subtitle: "Dailies and to-dos due over the next 7 days",
            Category: NavigationCategory,
            Icon: Icons.Material.Filled.CalendarMonth,
            ShortcutBadge: "G U",
            Action: () => NavigateAsync("/upcoming"),
            Keywords: ["upcoming", "calendar", "schedule", "due", "next", "week"]));

        if (_uiSession is not null)
        {
            list.Add(new(
                Id: "nav-ai-assistant",
                Title: "AI assistant",
                Subtitle: "Chat with your board helper and habit coach",
                Category: NavigationCategory,
                Icon: Icons.Material.Filled.SmartToy,
                Action: () =>
                {
                    _close();
                    _uiSession.SetAiDrawer(true);
                    return Task.CompletedTask;
                },
                Keywords: ["ai", "assistant", "coach", "chat", "mcp", "opencode", "llm", "plan"]));
        }

        list.Add(new(
            Id: "nav-settings",
            Title: "Settings",
            Subtitle: "Preferences, appearance, notifications",
            Category: NavigationCategory,
            Icon: Icons.Material.Filled.Settings,
            ShortcutBadge: "G P",
            Action: () => NavigateAsync(SettingsPath),
            Keywords: ["settings", "preferences", "config", "account", "profile"]));

        list.Add(new(
            Id: "nav-settings-account",
            Title: "Account and data settings",
            Subtitle: "Profile, export, password, and account management",
            Category: NavigationCategory,
            Icon: Icons.Material.Filled.ManageAccounts,
            Action: () => NavigateAsync(SettingsPath),
            Keywords: ["account", "password", "profile", "export", "backup"]));

        list.Add(new(
            Id: "nav-settings-notifications",
            Title: "Notification settings",
            Subtitle: "Reminders, alerts, and quiet hours",
            Category: NavigationCategory,
            Icon: Icons.Material.Filled.Notifications,
            Action: () => NavigateAsync(SettingsPath),
            Keywords: ["notifications", "alerts", "reminders", "sound"]));

        list.Add(new(
            Id: "nav-mcp-setup",
            Title: "Connect external AI (MCP)",
            Subtitle: "Get a token and configs for Claude, OpenCode, Hermes",
            Category: NavigationCategory,
            Icon: Icons.Material.Filled.Hub,
            Action: () => NavigateToMcpSetupAsync(),
            Keywords: ["mcp", "opencode", "hermes", "claude", "cursor", "vscode", "token", "external", "connect", "setup"]));
    }

    public void AddArchiveCommand(List<CommandItem> list)
    {
        list.Add(new(
            Id: "action-archive",
            Title: "Open archive",
            Subtitle: "Browse and restore archived board items",
            Category: "Actions",
            Icon: Icons.Material.Filled.Archive,
            Action: OpenArchiveAsync,
            Keywords: ["archive", "archived", "restore", "history"]));
    }

    public void AddYesterdayRetroCommand(List<CommandItem> list)
    {
        list.Add(new(
            Id: "action-yesterday-retro",
            Title: "Yesterday's dailies check-in",
            Subtitle: "Review and backdate unfinished dailies from yesterday",
            Category: "Actions",
            Icon: Icons.Material.Filled.History,
            Action: OpenYesterdayRetroAsync,
            Keywords: ["yesterday", "retro", "dailies", "checkin", "backdate", "review"]));
    }

    public void AddAppearanceCommands(List<CommandItem> list)
    {
        list.Add(new(
            Id: "theme-dark",
            Title: "Dark theme",
            Subtitle: "Switch app appearance to dark mode",
            Category: AppearanceCategory,
            Icon: Icons.Material.Filled.DarkMode,
            Action: () => SetThemeAsync(AppTheme.Dark),
            Keywords: [KwTheme, "dark", "mode", "color", "night"]));

        list.Add(new(
            Id: "theme-light",
            Title: "Light theme",
            Subtitle: "Switch app appearance to light mode",
            Category: AppearanceCategory,
            Icon: Icons.Material.Filled.LightMode,
            Action: () => SetThemeAsync(AppTheme.Light),
            Keywords: [KwTheme, "light", "mode", "color", "day"]));

        list.Add(new(
            Id: "theme-system",
            Title: "System theme",
            Subtitle: "Sync app appearance with operating system",
            Category: AppearanceCategory,
            Icon: Icons.Material.Filled.SettingsBrightness,
            Action: () => SetThemeAsync(AppTheme.System),
            Keywords: [KwTheme, "system", "auto", "os"]));

        list.Add(new(
            Id: "pref-toggle-shortcuts",
            Title: "Toggle keyboard shortcuts",
            Subtitle: "Enable or disable global hotkeys across the app",
            Category: AppearanceCategory,
            Icon: Icons.Material.Filled.Keyboard,
            Action: ToggleKeyboardShortcutsAsync,
            Keywords: ["keyboard", "shortcuts", "hotkeys", "keys"]));
    }

    public Task NavigateAsync(string path)
    {
        _close();
        _nav.NavigateTo(path);
        return Task.CompletedTask;
    }

    private Task NavigateToMcpSetupAsync()
    {
        _uiSession?.SettingsSectionIndex = 3;
        return NavigateAsync(SettingsPath);
    }

    private async Task OpenArchiveAsync()
    {
        _close();
        await _dialogs.ShowAsync<ArchivedItemsDialog>(string.Empty, DialogDefaults.Wide);
    }
    private async Task SetThemeAsync(AppTheme theme)
    {
        _close();
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
    private async Task OpenYesterdayRetroAsync()
    {
        _close();
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
            await _notifyRefresh();
        }
        catch
        {
            await _notifier.NotifyAsync("Could not open yesterday's dailies check-in.", Severity.Error);
        }
    }

    private async Task ToggleKeyboardShortcutsAsync()
    {
        _close();
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
}
