#pragma warning disable S4487, S2325, S3459, S8949, S6966 // Razor markup reads and assigns the flagged fields through @if bindings and @ref. The flagged methods call injected services and ComponentBase members. Sonar analyzes this code-behind file in isolation.
using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class MainBoard
{
    private bool _boardClientScriptsStarted;
    private bool _shortcutsEnabled = true;
    private DotNetObjectReference<MainBoard>? _selfRef;
    private ElementReference _swipeAreaRef;
    private bool? _isMobileViewport;
    private bool? _isPhoneViewport;
    private bool _swipeInitialized;
    private bool _onboardingDismissed;
    private bool _onboardingChecked;
    private string? _lastComposeQueryUri;

    private bool ShowOnboarding =>
        _initialLoadComplete && _loadError is null && !HasBoardItems() && _onboardingChecked && !_onboardingDismissed;

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        _ = InvokeAsync(async () =>
        {
            await LoadShortcutsPreferenceAsync();
            StateHasChanged();
        });
    }

    private async Task LoadShortcutsPreferenceAsync()
    {
        try
        {
            var prefs = await PreferencesService.GetAsync();
            _shortcutsEnabled = prefs.EnableKeyboardShortcuts;
        }
        catch
        {
            // Ignore the error if settings storage fails.
        }
    }

    private async ValueTask SafeInvokeVoidAsync(string identifier, params object?[]? args)
    {
        try
        {
            await JS.InvokeVoidAsync(identifier, args);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or TaskCanceledException or InvalidOperationException)
        {
            // Ignored during page navigation or disposal.
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Preload SortableJS and the board reordering scripts during the initial data load.
            _ = PreloadSortableScriptsAsync();
            _ = LoadOnboardingDismissedAsync();
        }

        if (_initialLoadComplete && _loadError is null)
        {
            await TryHandleComposeQueryAsync();
        }

        if (_initialLoadComplete && _loadError is null && !_boardClientScriptsStarted)
        {
            await StartBoardClientScriptsAsync();
        }

        if (_boardClientScriptsStarted)
        {
            await EnsureSwipeAsync();
        }
    }

    private async Task LoadOnboardingDismissedAsync()
    {
        try
        {
            var value = await JS.InvokeAsync<string?>("localStorage.getItem", "habitinator-onboarding-dismissed");
            _onboardingDismissed = string.Equals(value, "1", StringComparison.Ordinal);
        }
        catch
        {
            _onboardingDismissed = false;
        }
        finally
        {
            _onboardingChecked = true;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task OnboardingDismissAsync()
    {
        _onboardingDismissed = true;
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", "habitinator-onboarding-dismissed", "1");
        }
        catch
        {
            // Best effort. The board still hides the chooser once items exist.
        }

        StateHasChanged();
    }

    private Task OnboardingStartFromGoalAsync()
    {
        UiSessionState.PendingAiPrompt = "Help me turn my goal into habits, dailies, and todos. My goal is: ";
        UiSessionState.SetAiDrawer(true);
        return Task.CompletedTask;
    }

    private async Task OnboardingUseTemplateAsync(string templateKey)
    {
        var template = BoardStarterTemplates.Find(templateKey);
        if (template is null)
        {
            return;
        }

        try
        {
            using var batch = UndoService.BeginBatch($"Added {template.Name} template");
            foreach (var spec in template.Items)
            {
                var created = await BoardDataService.CreateItemAsync(spec.Section, spec.Title);
                if (spec.Tags is null && spec.Section != BoardSection.Habit)
                {
                    continue;
                }

                if (spec.Section == BoardSection.Habit)
                {
                    await BoardDataService.UpdateHabitAsync(created.Id, UpdateHabitArgs.From(created) with
                    {
                        Tags = spec.Tags ?? created.Tags,
                        ResetPeriod = spec.ResetPeriod ?? created.ResetPeriod,
                        TargetCount = spec.TargetCount
                    });
                }
                else if (spec.Section == BoardSection.Daily)
                {
                    await BoardDataService.UpdateDailyAsync(created.Id, UpdateDailyArgs.From(created) with { Tags = spec.Tags });
                }
                else
                {
                    await BoardDataService.UpdateTodoAsync(created.Id, UpdateTodoArgs.From(created) with { Tags = spec.Tags });
                }
            }

            await OnBoardChanged();
            await Notifier.NotifyAsync($"Added the {template.Name} starter board.", MudBlazor.Severity.Success);
        }
        catch
        {
            await Notifier.NotifyAsync("Couldn't add the starter board. Try again.", MudBlazor.Severity.Error);
        }
    }

    /// <summary>
    ///     Handles the PWA "New to-do" shortcut. The item opens straight in the editor so the
    ///     shortcut lands on the task, not on a board the user still has to scan.
    /// </summary>
    private async Task TryHandleComposeQueryAsync()
    {
        var uri = Nav.Uri;
        if (string.Equals(uri, _lastComposeQueryUri, StringComparison.Ordinal)
            || !TryGetQueryValue(uri, "compose", out var value)
            || !string.Equals(value, "todo", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastComposeQueryUri = uri;
        try
        {
            await CreateItemPublicAsync(BoardSection.Todo);
        }
        finally
        {
            // Remove the shortcut query only after the editor closes. Navigating
            // while the dialog is open can tear down its provider and leave the
            // create task waiting on a result that never arrives.
            Nav.NavigateTo(Nav.GetUriWithQueryParameter("compose", (string?)null), replace: true);
        }
    }

    private static bool TryGetQueryValue(string uri, string key, out string? value)
    {
        value = null;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        foreach (var part in parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var name = separator < 0 ? part : part[..separator];
            if (!string.Equals(Uri.UnescapeDataString(name), key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = separator < 0
                ? string.Empty
                : Uri.UnescapeDataString(part[(separator + 1)..].Replace("+", " ", StringComparison.Ordinal));
            return true;
        }

        return false;
    }

    private async Task OnboardingLoadDemoAsync()
    {
        try
        {
            var http = HttpFactory.CreateClient("api");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await http.PostAsync("api/board/seed-demo", null, cts.Token);
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            await Notifier.NotifyAsync("Couldn't load demo. Try again.", MudBlazor.Severity.Error);
            return;
        }

        await OnBoardChanged();
    }

    private async Task PreloadSortableScriptsAsync()
    {
        try
        {
            await Task.WhenAll(
                LoadBoardScriptAsync("_content/App.Shared.RCL/js/sortable.min.js"),
                LoadBoardScriptAsync("_content/App.Shared.RCL/js/boardSortable.js"));
        }
        catch (Exception)
        {
            // Ignored during background preload
        }
    }

    private async Task LoadBoardScriptAsync(string path)
    {
        await JS.InvokeVoidAsync(HabitinatorLoadScriptFunction, path);
    }

    private async Task StartBoardClientScriptsAsync()
    {
        _boardClientScriptsStarted = true;

        // The retro prompt reads local state only, so it starts before the board
        // scripts load. A script fetch or viewport setup delay then cannot hold
        // up the prompt.
        _dailyRetroClientReady = true;
        _ = InvokeAsync(OpenRetroPromptAsync);
        _ = RefreshStreaksAsync();

        try
        {
            _selfRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("HabitinatorKeyboardShortcuts.start", _selfRef);

            await JS.InvokeVoidAsync(
                HabitinatorLoadScriptFunction,
                "_content/App.Shared.RCL/js/boardViewport.js");
            try
            {
                _isMobileViewport = await JS.InvokeAsync<bool>("HabitinatorBoardViewport.isMobile");
                _isPhoneViewport = await JS.InvokeAsync<bool>("HabitinatorBoardViewport.isPhone");
            }
            catch (Exception ex) when (ex is JSDisconnectedException or JSException or TaskCanceledException or InvalidOperationException)
            {
                _isMobileViewport = false;
                _isPhoneViewport = false;
            }
            await JS.InvokeVoidAsync("HabitinatorBoardViewport.watch", _selfRef);
            if (_isMobileViewport is true)
            {
                // The pre-interactive render used the desktop branch. Mount the mobile branch now.
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (JSDisconnectedException)
        {
            // Ignored during page navigation
        }
        catch (TaskCanceledException)
        {
            // Ignored during page navigation
        }
        catch (InvalidOperationException)
        {
            // Ignored during page navigation
        }
        catch (JSException)
        {
            _boardClientScriptsStarted = false;
        }
    }

    private async Task OpenRetroPromptAsync()
    {
        try
        {
            await TryOpenDailyYesterdayRetroIfNeededAsync();
            await InvokeAsync(StateHasChanged);
        }
        catch
        {
            // Best effort post-paint dialog. Retry happens on next load.
        }
    }

    /// <summary>
    ///     Called by <c>HabitinatorBoardViewport</c> when the viewport crosses the mobile breakpoint.
    ///     Only one column branch stays mounted, so a switch mounts the other branch and moves
    ///     swipe handling with it.
    /// </summary>
    [JSInvokable]
    public async Task OnViewportChanged(bool isMobile)
    {
        if (_isMobileViewport == isMobile)
        {
            return;
        }

        _isMobileViewport = isMobile;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    ///     <c>HabitinatorBoardViewport</c> calls this when the viewport crosses the phone breakpoint.
    ///     On phones the search row becomes a bottom thumb bar. The tags menu opens upward.
    /// </summary>
    [JSInvokable]
    public async Task OnPhoneViewportChanged(bool isPhone)
    {
        if (_isPhoneViewport == isPhone)
        {
            return;
        }

        _isPhoneViewport = isPhone;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    ///     Swipe gestures work in the mobile branch only. The swipe area ref exists only while
    ///     that branch is mounted. This method starts swipe handling from the ref and tears it
    ///     down when the branch unmounts.
    /// </summary>
    private async Task EnsureSwipeAsync()
    {
        if (_isMobileViewport is true && !_swipeInitialized && _swipeAreaRef.Context is not null)
        {
            try
            {
                await JS.InvokeVoidAsync(
                    HabitinatorLoadScriptFunction,
                    "_content/App.Shared.RCL/js/boardSwipe.js");
                _selfRef ??= DotNetObjectReference.Create(this);
                await JS.InvokeVoidAsync("HabitinatorBoardSwipe.init", _swipeAreaRef, _selfRef);
                _swipeInitialized = true;
            }
            catch (Exception ex) when (ex is JSDisconnectedException or JSException or TaskCanceledException or InvalidOperationException)
            {
                // Ignored during page navigation or disposal.
            }
        }
        else if (_isMobileViewport is not true && _swipeInitialized)
        {
            await SafeInvokeVoidAsync("HabitinatorBoardSwipe.destroy", _swipeAreaRef);
            _swipeInitialized = false;
        }
    }
}
