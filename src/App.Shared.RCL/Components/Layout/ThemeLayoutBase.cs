using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Components.Layout;

/// <summary>
///     Shared theme engine for the web and MAUI main layouts: preferences loading, dark-mode
///     resolution and watch, HTML theme sync, keyboard shortcut wiring, and first-render script
///     initialization. Hosts override the platform-specific parts
///     <see cref="GetSystemDarkMode" /> and <see cref="SyncNativeThemeAsync" />.
/// </summary>
public abstract class ThemeLayoutBase : LayoutComponentBase, IDisposable
{
    [Inject] protected IUserPreferencesService PreferencesService { get; set; } = default!;
    [Inject] protected IUserDateFormatService DateFormatService { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    protected UserPreferences? Preferences { get; set; }
    protected bool IsDarkMode { get; set; } = true;
    protected MudThemeProvider? MudThemeProviderRef { get; set; }
    protected DotNetObjectReference<ThemeLayoutBase>? LayoutSelfRef { get; set; }

    protected override void OnInitialized()
    {
        PreferencesService.Changed += OnPreferencesChanged;
        Nav.LocationChanged += OnLocationChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        // 1. Resolve the theme first so the app renders correctly before it is revealed.
        await InitializeThemeAsync();

        // 2. Initialize the board visibility script and keyboard shortcuts.
        await InitializeScriptsAsync();

        // 3. Host-specific first-render work, e.g. startup update check or splash removal.
        await OnAfterFirstRenderAsync();
    }

    protected async Task InitializeThemeAsync()
    {
        try
        {
            if (MudThemeProviderRef != null)
            {
                await ResolveThemeAsync();
                await MudThemeProviderRef.WatchSystemDarkModeAsync(OnSystemPreferenceChanged);
            }
        }
        catch
        {
            // Ignore theme watcher failures, e.g. during prerendering.
        }
    }

    protected async Task InitializeScriptsAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("habitinatorLoadScript", "_content/App.Shared.RCL/js/boardVisibility.js");
            LayoutSelfRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("HabitinatorKeyboardShortcuts.startGlobal", LayoutSelfRef);
            await SyncKeyboardShortcutsEnabledAsync();
        }
        catch
        {
            // Ignore JS/shortcut initialization errors, e.g. during prerendering or on test hosts.
        }
    }

    protected virtual Task OnAfterFirstRenderAsync() => Task.CompletedTask;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        _ = InvokeAsync(LoadPreferencesAsync);
    }

    protected async Task LoadPreferencesAsync()
    {
        Preferences = await PreferencesService.GetAsync();
        DateFormatService.ApplyFromPreferences(Preferences);
        await ResolveThemeAsync();
        await SyncKeyboardShortcutsEnabledAsync();
    }

    protected async Task SyncKeyboardShortcutsEnabledAsync()
    {
        if (Preferences is null)
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("HabitinatorKeyboardShortcuts.setEnabled", Preferences.EnableKeyboardShortcuts);
        }
        catch
        {
            // Ignore if JS is not available, e.g. during prerendering.
        }
    }

    protected async Task SyncHtmlThemeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("habitinatorSetTheme", IsDarkMode ? "dark" : "light");
            await SyncNativeThemeAsync(IsDarkMode);
        }
        catch
        {
            // Ignore if JS is not available, e.g. during prerendering.
        }
    }

    protected async Task ResolveThemeAsync()
    {
        if (Preferences is null)
        {
            return;
        }

        if (Preferences.Theme == AppTheme.Light)
        {
            IsDarkMode = false;
        }
        else if (Preferences.Theme == AppTheme.Dark)
        {
            IsDarkMode = true;
        }
        else // System
        {
            IsDarkMode = MudThemeProviderRef != null
                ? await MudThemeProviderRef.GetSystemDarkModeAsync()
                : GetSystemDarkMode();
        }

        await SyncHtmlThemeAsync();
        await InvokeAsync(StateHasChanged);
    }

    protected async Task OnSystemPreferenceChanged(bool isDarkMode)
    {
        if (Preferences?.Theme == AppTheme.System)
        {
            IsDarkMode = isDarkMode;
            await SyncHtmlThemeAsync();
            StateHasChanged();
        }
    }

    /// <summary>Reads the OS dark-mode preference. Platform-specific: JS matchMedia on web, MAUI app theme on mobile.</summary>
    protected abstract bool GetSystemDarkMode();

    /// <summary>Applies the resolved theme outside Blazor. Web is a no-op. MAUI syncs the native status bar.</summary>
    protected virtual Task SyncNativeThemeAsync(bool isDarkMode) => Task.CompletedTask;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        PreferencesService.Changed -= OnPreferencesChanged;
        Nav.LocationChanged -= OnLocationChanged;
        LayoutSelfRef?.Dispose();
    }
}
