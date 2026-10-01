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
    Func<Task> notifyRefresh)
{
    private readonly NavigationManager _nav = nav;
    private readonly IBoardDataService _boardData = boardData;
    private readonly IDialogService _dialogs = dialogs;
    private readonly IUserPreferencesService _preferences = preferences;
    private readonly IUserNotifier _notifier = notifier;
    private readonly Action _close = close;
    private readonly Func<Task> _notifyRefresh = notifyRefresh;

    public void AddNavigationCommands(List<CommandItem> list)
    {
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

    public void AddOnboardingCommand(List<CommandItem> list)
    {
        list.Add(new(
            Id: "action-onboarding",
            Title: "Getting started guide",
            Subtitle: "View welcome tutorial and key shortcuts",
            Category: "Actions",
            Icon: Icons.Material.Filled.HelpOutline,
            Action: OpenOnboardingAsync,
            Keywords: ["guide", "help", "tutorial", "onboarding", "welcome", "shortcuts"]));
    }

    public void AddAppearanceCommands(List<CommandItem> list)
    {
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
    }

    public Task NavigateAsync(string path)
    {
        _close();
        _nav.NavigateTo(path);
        return Task.CompletedTask;
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

    private async Task OpenOnboardingAsync()
    {
        _close();
        await _dialogs.ShowAsync<OnboardingDialog>(string.Empty, DialogDefaults.SmallEditor);
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
