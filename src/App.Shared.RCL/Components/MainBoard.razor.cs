#pragma warning disable S4487, S2325, S3459, S8949, S6966 // Flagged fields are read/assigned by the Razor markup part (@if bindings, @ref), flagged methods use services injected there or ComponentBase members; Sonar sees this code-behind file in isolation.
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
    private List<BoardItem> _filteredHabits = [];
    private List<BoardItem> _filteredDailies = [];
    private List<BoardItem> _filteredTodos = [];
    private bool _isLoading;
    private bool _initialLoadComplete;
    private string? _loadError;
    private string _searchText = string.Empty;
    private readonly HashSet<string> _selectedFilterTags = [];
    private bool _dailyRetroSessionOffered;
    private bool _dailyRetroClientReady;
    private bool _boardClientScriptsStarted;
    private bool _shortcutsEnabled = true;
    private DotNetObjectReference<BoardRemoteNotifyBridge>? _visibilityRef;
    private DotNetObjectReference<MainBoard>? _selfRef;
    private ElementReference _swipeAreaRef;
    private bool? _isMobileViewport;
    private bool? _isPhoneViewport;
    private bool _swipeInitialized;
    private DateTimeOffset _lastLocalMutationTime = DateTimeOffset.MinValue;
    private PersistingComponentStateSubscription _subscription;
    private bool _onboardingDismissed;
    private bool _onboardingChecked;

    private bool ShowOnboarding =>
        _initialLoadComplete && _loadError is null && !HasBoardItems() && _onboardingChecked && !_onboardingDismissed;

    private List<BoardItem> FilteredHabits => _filteredHabits;
    private List<BoardItem> FilteredDailies => _filteredDailies;

    private int DailiesDueOpenCount => _cachedDailiesDueOpenCount;

    private List<BoardItem> FilteredTodos => _filteredTodos;

    private int OpenTodoCount => _cachedOpenTodoCount;

    private int _mobileSectionIndex;
    private ColumnSpec[] _cachedBoardColumns = [];
    private List<string> _cachedTagMenu = [];
    private string _cachedTagsTriggerTitle = "Filter by tags";
    private int _cachedDailiesDueOpenCount;
    private int _cachedOpenTodoCount;
    private CancellationTokenSource? _searchDebounceCts;
    private MudMenu? _tagsMenu;

    private sealed record ColumnSpec(BoardSection Section, IReadOnlyList<BoardItem> Items, bool IsExcluded);

    private ColumnSpec[] BoardColumns => _cachedBoardColumns;

    private bool IsFilteredOut(BoardSection section) => section switch
    {
        BoardSection.Habit => Habits.Count > 0 && FilteredHabits.Count == 0,
        BoardSection.Daily => Dailies.Count > 0 && FilteredDailies.Count == 0,
        _ => Todos.Count > 0 && FilteredTodos.Count == 0
    };

    private string GetSectionSwitcherClass(int index) =>
        index == _mobileSectionIndex
            ? "board-section-switcher__btn board-section-switcher__btn--active"
            : "board-section-switcher__btn";

    private string GetTagsTriggerClass() =>
        SelectedFilterTagCount > 0
            ? "board-trigger-btn board-trigger-btn--active"
            : "board-trigger-btn";

    private string GetTagsTriggerTitle() => _cachedTagsTriggerTitle;

    private Origin TagsMenuAnchor => _isPhoneViewport == true ? Origin.TopRight : Origin.BottomRight;

    private Origin TagsMenuTransform => _isPhoneViewport == true ? Origin.BottomRight : Origin.TopRight;

    private void OnMobileSectionKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is not ("ArrowLeft" or "ArrowRight" or "Home" or "End"))
        {
            return;
        }

        var next = e.Key switch
        {
            "ArrowLeft" => (_mobileSectionIndex + 2) % 3,
            "ArrowRight" => (_mobileSectionIndex + 1) % 3,
            "Home" => 0,
            _ => 2
        };
        SetMobileSectionIndex(next);
        StateHasChanged();
    }

    private void SetMobileSectionIndex(int index)
    {
        _mobileSectionIndex = index;
        UiSessionState.MobileSectionIndex = index;
    }

    private List<string> GetAllTagNamesForMenu() => _cachedTagMenu;

    private int SelectedFilterTagCount => _selectedFilterTags.Count;

    protected override Task OnInitializedAsync()
    {
        _subscription = ApplicationState.RegisterOnPersisting(PersistBoardData);
        _searchText = UiSessionState.SearchText;
        _selectedFilterTags.Clear();
        foreach (var tag in UiSessionState.SelectedFilterTags)
        {
            _selectedFilterTags.Add(tag);
        }
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
            await LoadAsync(LoadBoardAsync);
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
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
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

    private void OnBoardSyncStatusChanged(object? sender, EventArgs e)
    {
        var key = (BoardSync.IsOffline, BoardSync.IsSyncing, BoardSync.PendingCount, BoardSync.SyncProblemMessage);
        if (key == _lastSyncStatusKey)
        {
            return;
        }
        _lastSyncStatusKey = key;
        _ = InvokeAsync(StateHasChanged);
    }

    private (bool IsOffline, bool IsSyncing, int PendingCount, string? ProblemMessage)? _lastSyncStatusKey;
    private bool _remoteRefreshInFlight;
    private DateTimeOffset _lastRemoteRefreshUtc = DateTimeOffset.MinValue;

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
            // Ignore if settings storage fails
        }
    }

    /// <summary>
    ///     Marshals server-pushed refreshes onto the Blazor dispatcher (SignalR/JS callbacks are not on the renderer sync
    ///     context).
    /// </summary>
    private Task HandleRemoteBoardRefreshedAsync()
    {
        return InvokeAsync(OnRemoteBoardRefreshedAsync);
    }

    private async Task OnRemoteBoardRefreshedAsync()
    {
        // Skip reloading from remote SignalR notifications if we recently performed a local mutation,
        // as the local action has already fetched the latest snapshot.
        if (DateTimeOffset.UtcNow - _lastLocalMutationTime < TimeSpan.FromSeconds(2.0))
        {
            return;
        }
        if (_remoteRefreshInFlight || DateTimeOffset.UtcNow - _lastRemoteRefreshUtc < TimeSpan.FromMilliseconds(500))
        {
            return;
        }
        _remoteRefreshInFlight = true;
        try
        {
            await LoadBoardAsync();
            await RefreshStreaksAsync();
        }
        catch
        {
            // keep existing UI. User can use Retry
        }
        finally
        {
            _remoteRefreshInFlight = false;
            _lastRemoteRefreshUtc = DateTimeOffset.UtcNow;
        }

        await InvokeAsync(StateHasChanged);
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
            await SafeInvokeVoidAsync("HabitinatorBoardVisibility.stop");
            await SafeInvokeVoidAsync("HabitinatorKeyboardShortcuts.stop");
        }

        _visibilityRef?.Dispose();
        _visibilityRef = null;
        _selfRef?.Dispose();
        _selfRef = null;
        _subscription.Dispose();
        GC.SuppressFinalize(this);
    }

    private async ValueTask SafeInvokeVoidAsync(string identifier, params object?[]? args)
    {
        try
        {
            await JS.InvokeVoidAsync(identifier, args);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or TaskCanceledException or InvalidOperationException)
        {
            // Ignored during page navigation/disposal
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Eagerly preload SortableJS and board reordering scripts during initial data load
            _ = PreloadSortableScriptsAsync();
            _ = LoadOnboardingDismissedAsync();
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
        try
        {
            await JS.InvokeVoidAsync(
                HabitinatorLoadScriptFunction,
                "_content/App.Shared.RCL/js/boardVisibility.js");

            _visibilityRef = DotNetObjectReference.Create(BoardNotifyBridge);
            await JS.InvokeVoidAsync("HabitinatorBoardVisibility.start", _visibilityRef);

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

            _dailyRetroClientReady = true;
            _ = RefreshStreaksAsync();
            // The retro dialog is post-paint work. Open it off the render path
            // so swipe and viewport setup stay responsive.
            _ = OpenRetroAfterScriptsAsync();
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

    private async Task OpenRetroAfterScriptsAsync()
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
    /// Called by <c>HabitinatorBoardViewport</c> when the viewport crosses the mobile breakpoint.
    /// Only one column branch stays mounted, so flips mount the other branch and move swipe handling with it.
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
    /// HabitinatorBoardViewport calls this when the viewport crosses the phone breakpoint.
    /// On phones the search row becomes a bottom thumb bar. The tags menu opens upward.
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
    /// Swipe gestures work in the mobile branch only. The swipe area ref exists only while
    /// that branch is mounted. The handler starts init from the ref. The handler destroys on branch switch.
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

    private async Task OnBoardChanged()
    {
        _lastLocalMutationTime = DateTimeOffset.UtcNow;
        await LoadBoardAsync();
    }

    private async Task RetryLoadAsync()
    {
        await LoadAsync(LoadBoardAsync);
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

    private async Task LoadBoardAsync()
    {
        if (ApplicationState.TryTakeFromJson<BoardSnapshot>("board_snapshot", out var restored) && restored is not null)
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
    }

    private void RecomputeFilters()
    {
        _filteredHabits = FilterItems(Habits);
        _filteredDailies = FilterItems(Dailies);
        _filteredTodos = FilterItems(Todos);
        var today = DailySchedule.LocalToday(TimeZoneService);
        _cachedDailiesDueOpenCount = _filteredDailies.Count(d => DailySchedule.IsDueOnDate(d, today));
        _cachedOpenTodoCount = _filteredTodos.Count(t => !t.IsCompleted);
        _cachedTagMenu = Habits.Concat(Dailies).Concat(Todos)
            .SelectMany(i => BoardTagUtil.ParseTags(i.Tags))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _cachedTagsTriggerTitle = SelectedFilterTagCount == 0
            ? "Filter by tags"
            : $"Tags: {string.Join(", ", _selectedFilterTags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))}";
        _cachedBoardColumns =
        [
            new ColumnSpec(BoardSection.Habit, _filteredHabits, IsFilteredOut(BoardSection.Habit)),
            new ColumnSpec(BoardSection.Daily, _filteredDailies, IsFilteredOut(BoardSection.Daily)),
            new ColumnSpec(BoardSection.Todo, _filteredTodos, IsFilteredOut(BoardSection.Todo))
        ];
    }

    private List<BoardItem> FilterItems(IReadOnlyList<BoardItem> source)
    {
        return source.Where(MatchesGlobalFilters).ToList();
    }

    private bool MatchesGlobalFilters(BoardItem item)
    {
        return MatchesTagFilter(item) && MatchesSearchQuery(item);
    }

    private bool MatchesTagFilter(BoardItem item)
    {
        if (_selectedFilterTags.Count == 0)
        {
            return true;
        }

        return BoardTagUtil.ParseTags(item.Tags).Any(_selectedFilterTags.Contains);
    }

    private bool MatchesSearchQuery(BoardItem item)
    {
        var q = _searchText.Trim();
        if (q.Length == 0)
        {
            return true;
        }

        if (item.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (item.Notes is not null && item.Notes.Contains(q, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(item.ChecklistJson)
            && item.ChecklistJson.Contains(q, StringComparison.OrdinalIgnoreCase)
            && DailyChecklistJson.Parse(item.ChecklistJson).Any(line => line.Text.Contains(q, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (BoardTagUtil.ParseTags(item.Tags).Any(tag => tag.Contains(q, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private bool IsFilterTagSelected(string tag)
    {
        return _selectedFilterTags.Contains(tag);
    }

    private void OnFilterTagChanged(string tag, bool isChecked)
    {
        if (isChecked)
        {
            _selectedFilterTags.Add(tag);
        }
        else
        {
            _ = _selectedFilterTags.Remove(tag);
        }

        UiSessionState.SelectedFilterTags.Clear();
        foreach (var t in _selectedFilterTags)
        {
            UiSessionState.SelectedFilterTags.Add(t);
        }

        RecomputeFilters();
    }

    private void PruneStaleFilterTags()
    {
        if (_selectedFilterTags.Count == 0)
        {
            return;
        }

        var available = new HashSet<string>(GetAllTagNamesForMenu(), StringComparer.OrdinalIgnoreCase);
        _selectedFilterTags.RemoveWhere(f => !available.Contains(f));
    }

    private void ClearBoardFilters()
    {
        _searchText = string.Empty;
        _selectedFilterTags.Clear();
        UiSessionState.SearchText = string.Empty;
        UiSessionState.SelectedFilterTags.Clear();
        RecomputeFilters();
    }

    private void OnSearchTextChanged()
    {
        UiSessionState.SearchText = _searchText;
        try
        {
            _searchDebounceCts?.Cancel();
        }
        catch
        {
            // A canceled debounce is expected on rapid typing.
        }
        _searchDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = DebouncedRecomputeAsync(cts);
    }

    private async Task DebouncedRecomputeAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(180, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }
            RecomputeFilters();
            await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke won. Ignore.
        }
        finally
        {
            if (ReferenceEquals(_searchDebounceCts, cts))
            {
                _searchDebounceCts = null;
            }
            cts.Dispose();
        }
    }

    private async Task HandleTimerLogSavedAsync(TimeSpan duration)
    {
        _lastLocalMutationTime = DateTimeOffset.UtcNow;
        var result = await TimerSessionLog.LogStoppedSessionAsync(duration);
        // When board progress was made, the undo toast for that action is the notification.
        if (result.BoardUpdateFailed || !result.BoardProgressed)
        {
            await Notifier.NotifyAsync(result.UserMessage, result.BoardUpdateFailed ? Severity.Error : Severity.Success);
        }

        await LoadBoardAsync();
    }

    private async Task TryOpenDailyYesterdayRetroIfNeededAsync()
    {
        if (_dailyRetroSessionOffered)
        {
            return;
        }

        _dailyRetroSessionOffered = true;
        try
        {
            var prefs = await PreferencesService.GetAsync();
            var today = DailySchedule.LocalToday(TimeZoneService, prefs.DayStartLocalTime);
            var lastResolved = await DailyRetroStore.GetLastPromptResolvedLocalDateAsync();

            if (lastResolved == today)
            {
                return;
            }

            // The board renders from the local mirror first. A check-in done on
            // another device, for example the web page, reaches this device only
            // through sync. Pull before deciding so yesterday's work that is
            // already done elsewhere does not prompt again here.
            if (!BoardSync.IsPaused)
            {
                try
                {
                    using var syncCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await BoardSyncRequestor.SyncNowAsync(syncCts.Token);
                    await LoadBoardAsync();
                    prefs = await PreferencesService.GetAsync();
                    today = DailySchedule.LocalToday(TimeZoneService, prefs.DayStartLocalTime);
                }
                catch
                {
                    // The device is offline or slow. Fall through with the local mirror.
                }
            }

            var missed = DailySchedule.GetYesterdayUncompletedDailies(Dailies, today, TimeZoneService);
            if (missed.Count == 0)
            {
                return;
            }

            var yesterdayDate = today.AddDays(-1);
            var parameters = new DialogParameters<DailyYesterdayRetroDialog>
            {
                { x => x.DueOn, yesterdayDate },
                { x => x.Items, missed }
            };

            var dialog = await DialogService.ShowAsync<DailyYesterdayRetroDialog>(
                string.Empty, parameters, DialogDefaults.SmallEditor);
            var result = await dialog.Result;

            try
            {
                await DailyRetroStore.SetPromptResolvedForTodayAsync();
            }
            catch (Exception)
            {
                // Ignore error when saving daily retro status as it's non-critical local UI state
            }

            if (result is { Canceled: false, Data: List<Guid> committed } && committed.Count > 0)
            {
                _lastLocalMutationTime = DateTimeOffset.UtcNow;
                if (!BoardSync.IsPaused)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await BoardSyncRequestor.SyncNowAsync(cts.Token);
                    }
                    catch
                    {
                        // If offline then rely on periodic sync.
                    }
                }

                var label = committed.Count == 1 ? "1 daily" : $"{committed.Count} dailies";
                await Notifier.NotifyAsync($"You logged {label} for yesterday.", Severity.Success);
            }

            await LoadBoardAsync();
            await RefreshStreaksAsync();
        }
        catch (Exception)
        {
            _dailyRetroSessionOffered = false;
        }
    }

    private async Task RefreshStreaksAsync()
    {
        try
        {
            var streaks = await BoardDataService.GetStreakMapAsync();
            var anyChanged = false;
            for (var i = 0; i < Dailies.Count; i++)
            {
                var daily = Dailies[i];
                if (streaks.TryGetValue(daily.Id, out var s) && s != daily.Counter)
                {
                    Dailies[i] = daily with { Counter = s };
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                RecomputeFilters();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception)
        {
            // Silently ignore. Board will reflect updated streaks on next full load
        }
    }

    private void HandleUndoStateChanged(object? sender, EventArgs e)
    {
        var canUndo = UndoService.CanUndo;
        if (canUndo == _lastCanUndo)
        {
            return;
        }
        _lastCanUndo = canUndo;
        _ = InvokeAsync(StateHasChanged);
    }

    private bool? _lastCanUndo;

    private void HandleUndoPerformed(object? sender, EventArgs e)
    {
        _ = InvokeAsync(async () =>
        {
            _lastLocalMutationTime = DateTimeOffset.UtcNow;
            await LoadBoardAsync();
            await RefreshStreaksAsync();
            StateHasChanged();
        });
    }

    private async Task HandleUndoAsync()
    {
        await UndoService.UndoAsync();
    }

    [JSInvokable]
    public Task OnSwipeSectionAsync(int direction)
    {
        _mobileSectionIndex = direction > 0
            ? (_mobileSectionIndex + 1) % 3
            : (_mobileSectionIndex + 2) % 3;
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task OnCtrlZPressed()
    {
        if (UndoService.CanUndo)
        {
            await HandleUndoAsync();
        }
    }

    private bool _isCreatingItem;

    public async Task CreateItemPublicAsync(BoardSection section)
    {
        if (_isCreatingItem)
        {
            return;
        }

        _isCreatingItem = true;
        var defaultTitle = section switch
        {
            BoardSection.Habit => "New Habit",
            BoardSection.Daily => "New Daily",
            BoardSection.Todo => "New To-do",
            _ => "New Item"
        };
        try
        {
            var item = await BoardDataService.CreateItemAsync(section, defaultTitle);
            if (item is not null)
            {
                await OnBoardChanged();
                var result = await OpenItemEditorForCreatedAsync(section, item);
                if (result is { Canceled: false, Data: not null })
                {
                    await HandleCreatedItemDialogActionAsync(section, item.Id, result.Data);
                }

                // The dialog autosaves every change, so an item that is still identical to the
                // freshly created one was never edited. Drop it to avoid cluttering the board.
                var current = await BoardDataService.GetItemAsync(item.Id);
                if (current is not null && current == item)
                {
                    await BoardDataService.DeleteItemAsync(section, item.Id);
                }

                await OnBoardChanged();
            }
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Couldn't add item. Try again.", Severity.Error);
        }
        finally
        {
            _isCreatingItem = false;
        }
    }

    private async Task HandleCreatedItemDialogActionAsync(BoardSection section, Guid itemId, object data)
    {
        EditDialogAction? action = data switch
        {
            EditHabitDialogResult h => h.Action,
            EditDailyDialogResult d => d.Action,
            EditTodoDialogResult t => t.Action,
            _ => null
        };

        if (action == EditDialogAction.Archive)
        {
            await BoardDataService.ArchiveItemAsync(section, itemId);
        }
        else if (action == EditDialogAction.Delete)
        {
            await BoardDataService.DeleteItemAsync(section, itemId);
        }
    }

    private async Task<DialogResult?> OpenItemEditorForCreatedAsync(BoardSection section, BoardItem item)
    {
        var options = DialogDefaults.SmallEditor;

        var dialog = section switch
        {
            BoardSection.Habit => await DialogService.ShowAsync<EditHabitDialog>(
                string.Empty, new DialogParameters<EditHabitDialog> { { x => x.Item, item } }, options),
            BoardSection.Daily => await DialogService.ShowAsync<EditDailyDialog>(
                string.Empty, new DialogParameters<EditDailyDialog> { { x => x.Item, item } }, options),
            _ => await DialogService.ShowAsync<EditTodoDialog>(
                string.Empty, new DialogParameters<EditTodoDialog> { { x => x.Item, item } }, options)
        };

        return await dialog.Result;
    }

    private async Task OpenArchivedItemsDialogAsync()
    {
        try
        {
            if (_tagsMenu is not null)
            {
                await _tagsMenu.CloseMenuAsync();
            }
        }
        catch (Exception ex)
        {
            // Ignore close failures. The tags menu may already be closed. The handler logs the error and opens the archive dialog.
            System.Diagnostics.Debug.WriteLine(ex.Message);
        }

        var dialog = await DialogService.ShowAsync<ArchivedItemsDialog>(string.Empty, DialogDefaults.Wide);
        await dialog.Result;

        _lastLocalMutationTime = DateTimeOffset.UtcNow;
        await LoadBoardAsync();
    }
}
