#pragma warning disable S4487, S2325, S3459, S8949, S6966 // Razor markup reads and assigns the flagged fields through @if bindings and @ref. The flagged methods call injected services and ComponentBase members. Sonar analyzes this code-behind file in isolation.
using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class MainBoard : IAsyncDisposable
{
    private const string HabitinatorLoadScriptFunction = "habitinatorLoadScript";
    private List<BoardItem> Habits { get; set; } = [];
    private List<BoardItem> Dailies { get; set; } = [];
    private List<BoardItem> Todos { get; set; } = [];
    private bool _isLoading;
    private bool _initialLoadComplete;
    private string? _loadError;
    private PersistingComponentStateSubscription _subscription;

    protected override Task OnInitializedAsync()
    {
        _subscription = ApplicationState.RegisterOnPersisting(PersistBoardData);
        _searchText = UiSessionState.SearchText;
        _selectedFilterTags.Clear();
        foreach (var tag in UiSessionState.SelectedFilterTags)
        {
            _selectedFilterTags.Add(tag);
        }
        _requireAllFilterTags = UiSessionState.RequireAllFilterTags;
        _quickFilter = UiSessionState.QuickFilter;
        _mobileSectionIndex = UiSessionState.MobileSectionIndex;

        if (ApplicationState.TryTakeFromJson<BoardSnapshot>("board_snapshot", out var restored)
            && restored is not null
            && IsNonEmptySnapshot(restored))
        {
            Habits = restored.Habits.ToList();
            Dailies = restored.Dailies.ToList();
            Todos = restored.Todos.ToList();
            PruneStaleFilterTags();
            RecomputeFilters();
            _initialLoadComplete = true;
            InitialBoardLoad.MarkComplete();
        }
        else if (BoardDataService.TryGetCachedSnapshot(out var cached)
            && cached is not null
            && IsNonEmptySnapshot(cached))
        {
            Habits = cached.Habits.ToList();
            Dailies = cached.Dailies.ToList();
            Todos = cached.Todos.ToList();
            PruneStaleFilterTags();
            RecomputeFilters();
            _initialLoadComplete = true;
            InitialBoardLoad.MarkComplete();
        }

        if (!_initialLoadComplete)
        {
            // No usable rows yet. Show skeletons on first paint while the local
            // mirror loads in the background. Empty snapshots keep the loading
            // state. The board never flashes empty before the remote hydrate.
            _isLoading = true;
        }

        BoardSync.Changed += OnBoardSyncStatusChanged;
        UserDay.DayChanged += OnUserDayChanged;
        RemoteBoardRefresh.RegisterForRemoteRefresh(HandleRemoteBoardRefreshedAsync);
        UndoService.OnStateChanged += HandleUndoStateChanged;
        UndoService.OnUndoPerformed += HandleUndoPerformed;
        PreferencesService.Changed += OnPreferencesChanged;

        // Prefs, date format, and the SQLite mirror load together off the
        // render path. First paint shows cached rows or skeletons.
        _ = InitializeBoardAsync();
        return Task.CompletedTask;
    }

    private async Task InitializeBoardAsync()
    {
        // Initialize the shared day service first so "today" and the daily
        // rollover use the user's local calendar, not UTC.
        await SafeInitializeAsync(() => UserDay.InitializeAsync());

        // Prefs and date format load off the critical path. The board read
        // starts at once, so first paint shows the local mirror without
        // waiting for settings.
        var prefsTask = Task.WhenAll(
            SafeInitializeAsync(() => DateFormatService.InitializeAsync()),
            SafeInitializeAsync(LoadShortcutsPreferenceAsync));

        if (!_initialLoadComplete)
        {
            try
            {
                await LoadInitialBoardAsync();
            }
            catch
            {
                // LoadAsync records _loadError for the retry UI. Anything escaping
                // here, like a disposed scope or a snackbar failure, must not stall init.
            }
        }

        try
        {
            await prefsTask;
        }
        catch
        {
            // Best effort. Board renders with defaults.
        }

        // Preferences carry the day start. Re-normalize the rows already on screen and
        // recompute the due counts so the board and the retro prompt agree on
        // the current day, even when the prerendered snapshot is from just
        // before a rollover.
        ApplyLocalDayToBoardLists();

        try
        {
            await InvokeAsync(StateHasChanged);
        }
        catch
        {
            // The board was disposed before background init finished.
        }
    }

    private static bool IsNonEmptySnapshot(BoardSnapshot snapshot) =>
        snapshot.Habits.Count + snapshot.Dailies.Count + snapshot.Todos.Count > 0;

    /// <summary>
    ///     Applies the current local day to rows that may have been restored from
    ///     prerender or a cache built before the day boundary.
    /// </summary>
    private void ApplyLocalDayToBoardLists()
    {
        var today = UserDay.Today;
        Habits = [.. Habits.Select(h => BoardItemMapper.WithLocalDay(h, BoardSection.Habit, today))];
        Dailies = [.. Dailies.Select(d => BoardItemMapper.WithLocalDay(d, BoardSection.Daily, today))];
        RecomputeFilters();
        ApplyDefaultMobileSection();
    }

    private bool HasBoardItems() =>
        Habits.Count + Dailies.Count + Todos.Count > 0;

    private async Task LoadInitialBoardAsync()
    {
        if (_initialLoadComplete)
        {
            return;
        }

        try
        {
            await EnsureAuthReadyAsync();
            await LoadAsync(() => LoadBoardAsync());
            if (_loadError is not null)
            {
                _initialLoadComplete = true;
                InitialBoardLoad.MarkComplete();
                return;
            }

            if (HasBoardItems())
            {
                _initialLoadComplete = true;
                InitialBoardLoad.MarkComplete();
                return;
            }

            // Empty local mirror. Pull once while skeletons stay up. A returning
            // user never sees an empty board flash before remote rows arrive.
            // Data saver is on for the paused case. Sync now fills the mirror.
            try
            {
                if (!BoardSync.IsPaused)
                {
                    // A cold demo seed can take a few seconds. Keep the
                    // skeletons up long enough to get the first rows rather
                    // than painting an empty board.
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await BoardSyncRequestor.SyncNowAsync(cts.Token);
                }

                await LoadBoardAsync();
            }
            catch
            {
                // Offline or remote failed. The empty state below is real.
            }

            _initialLoadComplete = true;
            InitialBoardLoad.MarkComplete();
        }
        catch
        {
            _initialLoadComplete = true;
            InitialBoardLoad.MarkComplete();
        }
    }

    private async Task EnsureAuthReadyAsync()
    {
        // MainBoard mounts only inside an authorized view. On MAUI a logged-in
        // gate guards it instead. A false IsLoggedIn here means the session
        // provider has not caught up yet. The user is not anonymous.
        // The local read is keyed by user. A brief wait avoids an empty
        // snapshot and the empty-board flash.
        if (ClientSession.IsLoggedIn)
        {
            return;
        }

        try
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(object? sender, EventArgs e) => tcs.TrySetResult();
            ClientSession.Changed += OnChanged;
            try
            {
                if (ClientSession.IsLoggedIn)
                {
                    return;
                }

                await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                ClientSession.Changed -= OnChanged;
            }
        }
        catch
        {
            // The wait timed out or failed. The load below still runs.
        }
    }

    private static async Task SafeInitializeAsync(Func<Task> init)
    {
        try
        {
            await init();
        }
        catch
        {
            // Best effort. Callers fall back to defaults.
        }
    }

    private Task PersistBoardData()
    {
        // Prerender registers this before the local mirror loads. An
        // unconditional persist would cache an empty board and flash it on
        // the next start. Persist only usable rows.
        if (!_initialLoadComplete || _loadError is not null || !HasBoardItems())
        {
            return Task.CompletedTask;
        }

        var snapshot = new BoardSnapshot(
            Habits,
            Dailies,
            Todos
        );
        ApplicationState.PersistAsJson("board_snapshot", snapshot);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _searchDebounceCts?.Cancel();
        }
        catch
        {
            // Best effort during disposal.
        }
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = null;
        BoardSync.Changed -= OnBoardSyncStatusChanged;
        UserDay.DayChanged -= OnUserDayChanged;
        RemoteBoardRefresh.UnregisterForRemoteRefresh(HandleRemoteBoardRefreshedAsync);
        UndoService.OnStateChanged -= HandleUndoStateChanged;
        UndoService.OnUndoPerformed -= HandleUndoPerformed;
        PreferencesService.Changed -= OnPreferencesChanged;
        if (_boardClientScriptsStarted)
        {
            if (_swipeInitialized)
            {
                await SafeInvokeVoidAsync("HabitinatorBoardSwipe.destroy", _swipeAreaRef);
                _swipeInitialized = false;
            }
            await SafeInvokeVoidAsync("HabitinatorBoardViewport.unwatch");
            await SafeInvokeVoidAsync("HabitinatorKeyboardShortcuts.stop");
        }

        _selfRef?.Dispose();
        _selfRef = null;
        _subscription.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task OnBoardChanged()
    {
        _lastLocalMutationTime = DateTimeOffset.UtcNow;
        await LoadBoardAsync();
    }

    private async Task RetryLoadAsync()
    {
        await LoadAsync(() => LoadBoardAsync());
        if (_loadError is null && _dailyRetroClientReady)
        {
            await TryOpenDailyYesterdayRetroIfNeededAsync();
        }
    }

    private async Task LoadAsync(Func<Task> load)
    {
        _isLoading = true;
        _loadError = null;
        try
        {
            await load();
        }
        catch (Exception ex)
        {
            _loadError = ex is InvalidOperationException
                ? ex.Message
                : "Couldn't load board. Try again.";
            await Notifier.NotifyAsync(_loadError, MudBlazor.Severity.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadBoardAsync(bool usePrerenderedState = true)
    {
        if (usePrerenderedState
            && ApplicationState.TryTakeFromJson<BoardSnapshot>("board_snapshot", out var restored)
            && restored is not null)
        {
            Habits = restored.Habits.ToList();
            Dailies = restored.Dailies.ToList();
            Todos = restored.Todos.ToList();
        }
        else
        {
            var snapshot = await BoardDataService.GetSnapshotAsync();
            Habits = snapshot.Habits.ToList();
            Dailies = snapshot.Dailies.ToList();
            Todos = snapshot.Todos.ToList();
        }
        PruneStaleFilterTags();
        RecomputeFilters();
        ApplyDefaultMobileSection();
    }
}
