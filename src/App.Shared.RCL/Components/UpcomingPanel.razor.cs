#pragma warning disable S8949, S6966, IDE0028, IDE0042, CA1859
using System.Globalization;

using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class UpcomingPanel : IDisposable
{
    private enum UpcomingFilter { All, Dailies, Todos }
    private enum CalView { Day, Week, Month }

    private sealed record CalChip(string Title, string Hint, bool IsTodo, bool Done, bool IsRoutine);

    private bool _loading = true;
    private string? _error;
    private DateOnly _today;
    private BoardSnapshot? _snapshot;
    private IReadOnlyList<BoardItem> _overdue = [];
    private UpcomingFilter _filter = UpcomingFilter.All;
    private CalView _view = CalView.Day;
    private bool _bulkMoving;
    private readonly HashSet<Guid> _busy = [];
    private int _todayDailyScheduled;
    private int _todayDailyDone;
    private int _todayTodoTotal;
    private int _todayTodoDone;
    private int _todayRemainingCache;
    private int _selectedAllDailies;
    private int _selectedAllTodos;
    private DayOfWeek _weekStartDay = DayOfWeek.Monday;
    private IReadOnlyList<BoardItem> _selectedDailiesCache = [];
    private IReadOnlyList<BoardItem> _selectedTodosCache = [];
    private Dictionary<DateOnly, (IReadOnlyList<BoardItem> Dailies, IReadOnlyList<BoardItem> Todos)> _cellCache = new();
    private int _visibleYear;
    private int _visibleMonth;
    private DateOnly _selected;
    private bool _calInit;
    private bool _addingTodo;
    private string _newTodoTitle = string.Empty;

    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private IUndoService UndoService { get; set; } = default!;
    [Inject] private IUserPreferencesService PreferencesService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private ElementReference _calBodyRef;
    private DotNetObjectReference<UpcomingPanel>? _swipeRef;
    private bool _swipeInit;

    private bool ShowDailies => _filter != UpcomingFilter.Todos;
    private bool ShowTodos => _filter != UpcomingFilter.Dailies;
    private bool HasOverdueCard => ShowTodos && _overdue.Count > 0;
    private int OverdueCount => _overdue.Count;

    private const int OverduePageSize = 4;
    private bool _showAllOverdue;
    private IReadOnlyList<BoardItem> GetVisibleOverdue() => _showAllOverdue ? _overdue : _overdue.Take(OverduePageSize).ToList();

    private void ToggleOverdueExpanded() => _showAllOverdue = !_showAllOverdue;

    private int TodayRemaining => _todayRemainingCache;

    private int TodayDailyPct => _todayDailyScheduled == 0 ? 100 : (int)Math.Round(100.0 * _todayDailyDone / _todayDailyScheduled);
    private int TodayTodoPct => _todayTodoTotal == 0 ? 100 : (int)Math.Round(100.0 * _todayTodoDone / _todayTodoTotal);

    private int FilterAllCount => _selectedAllDailies + _selectedAllTodos;

    private int FilterDailiesCount => _selectedAllDailies;

    private int FilterTodosCount => _selectedAllTodos;

    private string MonthTitle => new DateOnly(_visibleYear, _visibleMonth, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    private DateOnly WeekStart
    {
        get
        {
            var diff = ((int)_selected.DayOfWeek - (int)_weekStartDay + 7) % 7;
            return _selected.AddDays(-diff);
        }
    }
    private string WeekRangeTitle
    {
        get
        {
            var s = WeekStart;
            var e = s.AddDays(6);
            return s.Year != e.Year
                ? $"{s:MMM d, yyyy} - {e:MMM d, yyyy}"
                : $"{s:MMM d} - {e:MMM d}, {e:yyyy}";
        }
    }
    private string StepperLabel => _view switch
    {
        CalView.Day => Math.Abs(_selected.DayNumber - _today.DayNumber) <= 1
            ? SelectedLabel
            : _selected.ToString("ddd, MMM d", CultureInfo.CurrentCulture),
        CalView.Week => WeekRangeTitle,
        _ => MonthTitle,
    };
    private string PrevLabel => _view switch
    {
        CalView.Day => "Previous day",
        CalView.Week => "Previous week",
        _ => "Previous month",
    };
    private string NextLabel => _view switch
    {
        CalView.Day => "Next day",
        CalView.Week => "Next week",
        _ => "Next month",
    };
    private string SelectedAnchorId => $"upcoming-day-{_selected:yyyy-MM-dd}";

    private string HeadlineText
    {
        get
        {
            // The overdue clause checks ShowTodos first. Under the Dailies
            // filter there is no overdue card to act on.
            if (ShowTodos && OverdueCount > 0 && TodayRemaining > 0)
            {
                return $"{OverdueCount} overdue, {TodayRemaining} due today";
            }
            if (ShowTodos && OverdueCount > 0)
            {
                return $"{OverdueCount} overdue to catch up on";
            }
            if (TodayRemaining > 0)
            {
                return $"{TodayRemaining} to do today";
            }
            return "Nothing due today";
        }
    }

    private IReadOnlyList<BoardItem> SelectedDailies => _selectedDailiesCache;

    private IReadOnlyList<BoardItem> SelectedTodos => _selectedTodosCache;

    private int SelectedVisibleCount => SelectedDailies.Count + SelectedTodos.Count;

    private string EmptyDayText => (_selectedAllDailies + _selectedAllTodos) > 0
        ? "Nothing matches this filter for the selected day."
        : "Nothing scheduled for this day.";

    private string SelectedLabel => DayLabel(_selected);

    protected override async Task OnInitializedAsync()
    {
        Nav.LocationChanged += OnLocationChanged;
        await LoadPersistedViewStateAsync();
        await LoadWeekStartAsync();
        ApplyQueryDate();
        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The swipe area mounts with the loaded calendar, not on first paint.
        if (!_loading && _error is null && !_swipeInit && _calBodyRef.Context is not null)
        {
            _swipeInit = true;
            try
            {
                await JS.InvokeVoidAsync("habitinatorLoadScript", "_content/App.Shared.RCL/js/boardSwipe.js");
                _swipeRef ??= DotNetObjectReference.Create(this);
                await JS.InvokeVoidAsync("HabitinatorBoardSwipe.init", _calBodyRef, _swipeRef, "OnUpcomingSwipeAsync", true);
            }
            catch (Exception ex) when (ex is JSDisconnectedException or JSException or TaskCanceledException or InvalidOperationException)
            {
                // Ignored during page navigation or hot reload.
                _swipeInit = false;
            }
        }
    }

    /// <summary>
    /// HabitinatorBoardSwipe calls this on a horizontal swipe over the calendar.
    /// Left means next. Right means previous. The chevron buttons work the same way.
    /// </summary>
    [JSInvokable]
    public Task OnUpcomingSwipeAsync(int direction)
    {
        if (direction > 0)
        {
            Next();
        }
        else
        {
            Prev();
        }

        StateHasChanged();
        return Task.CompletedTask;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (ApplyQueryDate())
        {
            RebuildCaches();
            _ = InvokeAsync(StateHasChanged);
        }
    }

    private bool ApplyQueryDate()
    {
        try
        {
            var uri = Nav.ToAbsoluteUri(Nav.Uri);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
            if (query.TryGetValue("date", out var rawValues))
            {
                var raw = rawValues.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(raw)
                    && DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    _selected = parsed;
                    _visibleYear = parsed.Year;
                    _visibleMonth = parsed.Month;
                    _calInit = true;
                    return true;
                }
            }
        }
        catch
        {
            // Query parsing is best effort.
        }

        return false;
    }

    private async Task ReloadAsync()
    {
        _loading = true;
        _error = null;
        StateHasChanged();
        await LoadAsync();
    }

    private async Task RefreshAsync()
    {
        if (_snapshot is null)
        {
            await LoadAsync();
            return;
        }
        var snapshot = await BoardData.GetSnapshotAsync();
        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(BoardSnapshot snapshot)
    {
        _snapshot = snapshot;
        _today = DailySchedule.LocalToday(TimeZoneService);
        _overdue = UpcomingSchedule.GetOverdueTodos(snapshot, _today);
        _todayDailyScheduled = snapshot.Dailies.Count(d => DailySchedule.IsScheduledOn(d, _today));
        var todayDue = snapshot.Dailies.Count(d => DailySchedule.IsDueOnDate(d, _today));
        _todayDailyDone = Math.Max(0, _todayDailyScheduled - todayDue);
        var todayTodos = snapshot.Todos.Where(t => t.TodoDueDate == _today).ToList();
        _todayTodoTotal = todayTodos.Count;
        _todayTodoDone = todayTodos.Count(t => t.IsCompleted);
        if (!_calInit)
        {
            _visibleYear = _today.Year;
            _visibleMonth = _today.Month;
            _selected = _today;
            _calInit = true;
        }
        RebuildCaches();
    }

    private void RebuildCaches()
    {
        if (_snapshot is null)
        {
            _todayRemainingCache = 0;
            _selectedDailiesCache = [];
            _selectedTodosCache = [];
            _cellCache = new();
            return;
        }
        if (_visibleYear <= 0 || _visibleMonth <= 0)
        {
            if (_today != default)
            {
                _visibleYear = _today.Year;
                _visibleMonth = _today.Month;
                if (_selected == default)
                {
                    _selected = _today;
                }
            }
            else
            {
                return;
            }
        }
        var showDailies = ShowDailies;
        var showTodos = ShowTodos;
        _todayRemainingCache = (showDailies ? _snapshot.Dailies.Count(d => DailySchedule.IsDueOnDate(d, _today)) : 0)
            + (showTodos ? _snapshot.Todos.Count(t => !t.IsCompleted && t.TodoDueDate == _today) : 0);
        var allDates = new HashSet<DateOnly>(GridCells());
        if (_view == CalView.Week)
        {
            allDates.UnionWith(WeekCells());
        }
        else if (_view == CalView.Day)
        {
            allDates.UnionWith(DayStripCells());
        }

        var byDate = new Dictionary<DateOnly, (List<BoardItem> Dailies, List<BoardItem> Todos)>(allDates.Count);
        foreach (var date in allDates)
        {
            byDate[date] = (new List<BoardItem>(), new List<BoardItem>());
        }
        if (showDailies)
        {
            foreach (var item in _snapshot.Dailies)
            {
                foreach (var date in allDates.Where(date => DailySchedule.IsScheduledOn(item, date)))
                {
                    byDate[date].Dailies.Add(item);
                }
            }
        }
        var openTodos = showTodos ? _snapshot.Todos.Where(t => !t.IsCompleted).ToList() : new List<BoardItem>();
        if (showTodos)
        {
            foreach (var t in openTodos.Where(t => t.TodoDueDate is { } due && byDate.ContainsKey(due)))
            {
                byDate[t.TodoDueDate!.Value].Todos.Add(t);
            }
        }
        foreach (var k in byDate.Keys.ToList())
        {
            var b = byDate[k];
            b.Dailies.Sort((a, b2) => string.Compare(a.Title, b2.Title, StringComparison.Ordinal));
            b.Todos.Sort((a, b2) => string.Compare(a.Title, b2.Title, StringComparison.Ordinal));
        }
        var next = new Dictionary<DateOnly, (IReadOnlyList<BoardItem> Dailies, IReadOnlyList<BoardItem> Todos)>(byDate.Count);
        foreach (var kv in byDate)
        {
            next[kv.Key] = (kv.Value.Dailies, kv.Value.Todos);
        }
        _cellCache = next;
        var allDailiesForSelected = _snapshot.Dailies.Where(d => DailySchedule.IsScheduledOn(d, _selected)).OrderBy(d => d.Title, StringComparer.Ordinal).ToList();
        var allTodosForSelected = _snapshot.Todos.Where(t => !t.IsCompleted && t.TodoDueDate == _selected).OrderBy(t => t.Title, StringComparer.Ordinal).ToList();
        _selectedAllDailies = allDailiesForSelected.Count;
        _selectedAllTodos = allTodosForSelected.Count;
        _selectedDailiesCache = showDailies ? allDailiesForSelected : [];
        _selectedTodosCache = showTodos ? allTodosForSelected : [];
    }

    private async Task LoadAsync()
    {
        try
        {
            await DateFormatService.InitializeAsync();
            var snapshot = await BoardData.GetSnapshotAsync();
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            try { await Notifier.NotifyAsync("Could not load upcoming schedule. Please try again.", Severity.Error); } catch (Exception) { /* Best-effort toast. Fallback UI already indicates the failure. */ }
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadWeekStartAsync()
    {
        try
        {
            var prefs = await PreferencesService.GetAsync();
            _weekStartDay = prefs.WeekStartDay;
        }
        catch
        {
            _weekStartDay = DayOfWeek.Monday;
        }
    }

    private void SetFilter(UpcomingFilter filter)
    {
        _filter = filter;
        RebuildCaches();
        _ = PersistViewStateAsync();
    }

    private void SetView(CalView view)
    {
        _view = view;
        RebuildCaches();
        _ = PersistViewStateAsync();
    }

    private async Task LoadPersistedViewStateAsync()
    {
        UpcomingViewState? state;
        try
        {
            state = await ViewState.GetAsync();
        }
        catch (Exception)
        {
            return;
        }

        if (state is null)
        {
            return;
        }

        if (Enum.TryParse<CalView>(state.View, ignoreCase: true, out var view))
        {
            _view = view;
        }
        else if (string.Equals(state.View, "Next7", StringComparison.OrdinalIgnoreCase))
        {
            // Retired rolling view maps to the calendar week.
            _view = CalView.Week;
        }

        if (Enum.TryParse<UpcomingFilter>(state.Filter, ignoreCase: true, out var filter))
        {
            _filter = filter;
        }
    }

    private async Task PersistViewStateAsync()
    {
        try
        {
            await ViewState.SetAsync(new UpcomingViewState(_view.ToString(), _filter.ToString()));
        }
        catch (Exception)
        {
            // Ignored. The view does not persist.
        }
    }

    private void SelectDay(DateOnly date)
    {
        _selected = date;
        _visibleYear = date.Year;
        _visibleMonth = date.Month;
        RebuildCaches();
    }

    private void PrevDay() => SelectDay(_selected.AddDays(-1));
    private void NextDay() => SelectDay(_selected.AddDays(1));

    private void Prev()
    {
        if (_view == CalView.Month)
        {
            PrevMonth();
        }
        else
        {
            SelectDay(_selected.AddDays(_view == CalView.Day ? -1 : -7));
        }
    }

    private void Next()
    {
        if (_view == CalView.Month)
        {
            NextMonth();
        }
        else
        {
            SelectDay(_selected.AddDays(_view == CalView.Day ? 1 : 7));
        }
    }

    private void PrevMonth()
    {
        var first = new DateOnly(_visibleYear, _visibleMonth, 1).AddMonths(-1);
        _visibleYear = first.Year;
        _visibleMonth = first.Month;
        RebuildCaches();
    }

    private void NextMonth()
    {
        var first = new DateOnly(_visibleYear, _visibleMonth, 1).AddMonths(1);
        _visibleYear = first.Year;
        _visibleMonth = first.Month;
        RebuildCaches();
    }

    private void GoToday()
    {
        _visibleYear = _today.Year;
        _visibleMonth = _today.Month;
        _selected = _today;
        RebuildCaches();
    }

    private static string TodoCompleteLabel(BoardItem todo) => $"Mark {todo.Title} complete";

    private bool IsBusy(Guid id) => _busy.Contains(id);

    private List<DateOnly> GridCells()
    {
        var first = new DateOnly(_visibleYear, _visibleMonth, 1);
        var lead = ((int)first.DayOfWeek - (int)_weekStartDay + 7) % 7;
        var start = first.AddDays(-lead);
        return Enumerable.Range(0, 42).Select(start.AddDays).ToList();
    }

    private List<DateOnly> WeekCells() => Enumerable.Range(0, 7).Select(WeekStart.AddDays).ToList();

    private List<DateOnly> DayStripCells() => Enumerable.Range(-3, 7).Select(_selected.AddDays).ToList();

    private List<DateOnly> RangeCells() => WeekCells();

    private string GridAriaLabel => $"Week of {WeekRangeTitle} calendar";

    private List<string> OrderedWeekdayLabels()
    {
        List<string> labels = [];
        for (var i = 0; i < 7; i++)
        {
            var dow = (DayOfWeek)(((int)_weekStartDay + i) % 7);
            labels.Add(CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(int)dow]);
        }
        return labels;
    }

    private IReadOnlyList<BoardItem> CellDailies(DateOnly date)
    {
        if (!ShowDailies || _snapshot is null)
        {
            return [];
        }
        if (_cellCache.TryGetValue(date, out var bucket))
        {
            return bucket.Dailies;
        }
        return [.. _snapshot.Dailies
            .Where(d => DailySchedule.IsScheduledOn(d, date))
            .OrderBy(d => d.Title, StringComparer.Ordinal)];
    }

    private IReadOnlyList<BoardItem> CellTodos(DateOnly date)
    {
        if (!ShowTodos || _snapshot is null)
        {
            return [];
        }
        if (_cellCache.TryGetValue(date, out var bucket))
        {
            return bucket.Todos;
        }
        return [.. _snapshot.Todos
            .Where(t => !t.IsCompleted && t.TodoDueDate == date)
            .OrderBy(t => t.Title, StringComparer.Ordinal)];
    }

    private static string DailyHint(BoardItem d) => $"{d.Title} ({UpcomingSchedule.DescribeDaily(d)})";

    private List<CalChip> CellChips(IReadOnlyList<BoardItem> dailies, IReadOnlyList<BoardItem> todos)
    {
        List<CalChip> chips = [];
        chips.AddRange(dailies.Select(d => new CalChip(d.Title, DailyHint(d), false, d.DailyLastCompletedOn == _today, false)));
        chips.AddRange(todos.Select(t => new CalChip(t.Title, t.TodoDueDate is { } due ? $"{t.Title} ({TodoDueRelativeText.Format(due, _today)})" : t.Title, true, false, false)));
        return chips;
    }

    /// <summary>
    /// Month cells collapse repeating dailies into one routine chip. Month cells show at most
    /// two to-dos. Week cells keep every item.
    /// </summary>
    private List<CalChip> MonthChips(IReadOnlyList<BoardItem> dailies, IReadOnlyList<BoardItem> todos)
    {
        if (_filter == UpcomingFilter.Dailies)
        {
            return CellChips(dailies, []);
        }
        List<CalChip> chips = [];
        if (ShowDailies && dailies.Count > 0)
        {
            if (dailies.Count == 1)
            {
                var d = dailies[0];
                chips.Add(new CalChip(d.Title, DailyHint(d), false, d.DailyLastCompletedOn == _today, false));
            }
            else
            {
                var hint = string.Join(", ", dailies.Take(6).Select(d => $"{d.Title} ({UpcomingSchedule.DescribeDaily(d)})"));
                chips.Add(new CalChip($"{dailies.Count} dailies", hint, false, false, true));
            }
        }
        var visibleTodos = todos.Take(2).Select(t => new CalChip(t.Title, t.TodoDueDate is { } due ? $"{t.Title} ({TodoDueRelativeText.Format(due, _today)})" : t.Title, true, false, false));
        chips.AddRange(visibleTodos);
        return chips;
    }

    /// <summary>
    /// Overflow count without rebuilding chip lists. The total mirrors
    /// <see cref="MonthChips"/>. Routine collapse compresses dailies to one.
    /// The Dailies filter keeps every daily.
    /// </summary>
    private int MonthOverflowCount(IReadOnlyList<BoardItem> dailies, IReadOnlyList<BoardItem> todos, int shown)
    {
        int total;
        if (_filter == UpcomingFilter.Dailies)
        {
            total = dailies.Count;
        }
        else if (dailies.Count > 1)
        {
            total = todos.Count + 1;
        }
        else
        {
            total = dailies.Count + todos.Count;
        }
        return Math.Max(0, total - shown);
    }

    private static string ChipClass(CalChip chip)
    {
        var cls = chip.IsTodo ? "cal-chip--todo" : "cal-chip--daily";
        if (chip.Done)
        {
            cls += " cal-chip--done";
        }
        if (chip.IsRoutine)
        {
            cls += " cal-chip--routine";
        }
        return cls;
    }

    private static string ChipLabel(CalChip chip) => chip.Title;

    private string CellClass(DateOnly date)
    {
        var cls = "cal-cell";
        if (_view == CalView.Month && date.Month != _visibleMonth)
        {
            cls += " cal-cell--outside";
        }
        if (date == _today)
        {
            cls += " cal-cell--today";
        }
        if (date == _selected)
        {
            cls += " cal-cell--selected";
        }
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            && date != _today && date != _selected)
        {
            cls += " cal-cell--weekend";
        }
        if (CellDailies(date).Count + CellTodos(date).Count >= 4)
        {
            cls += " cal-cell--busy";
        }
        return cls;
    }

    private string DayLabel(DateOnly date)
    {
        var offset = date.DayNumber - _today.DayNumber;
        return offset switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            _ => date.ToString("dddd", CultureInfo.CurrentCulture),
        };
    }

    private string CellAria(DateOnly date, IReadOnlyList<BoardItem> dailies, IReadOnlyList<BoardItem> todos)
    {
        var total = dailies.Count + todos.Count;
        if (total == 0)
        {
            return $"{DayLabel(date)}, {FormatDate(date)}, no items";
        }
        var names = dailies.Select(d => d.Title).Concat(todos.Select(t => t.Title)).Take(2).ToList();
        var suffix = total > names.Count ? $" and {total - names.Count} more" : string.Empty;
        return $"{DayLabel(date)}, {FormatDate(date)}, {total} items: {string.Join(", ", names)}{suffix}";
    }

    private bool IsCheckedToday(BoardItem daily) =>
        DailySchedule.IsCompletedForToday(daily.DailyLastCompletedOn, daily.IsCompleted, _today);

    private async Task ToggleTodayAsync(BoardItem daily)
    {
        try
        {
            await BoardData.ToggleItemAsync(BoardSection.Daily, daily.Id);
            await RefreshAsync();
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Could not update this daily. Check your connection. Try again.", Severity.Error);
        }
    }

    private async Task ToggleTodoAsync(BoardItem todo)
    {
        if (!_busy.Add(todo.Id))
        {
            return;
        }
        try
        {
            await BoardData.ToggleItemAsync(BoardSection.Todo, todo.Id);
            await RefreshAsync();
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Could not update this to-do. Check your connection. Try again.", Severity.Error);
        }
        finally
        {
            _busy.Remove(todo.Id);
        }
    }

    private async Task ShiftTodoAsync(BoardItem todo, int days)
    {
        if (todo.TodoDueDate is null || !_busy.Add(todo.Id))
        {
            return;
        }
        try
        {
            var next = todo.TodoDueDate.Value.AddDays(days);
            var args = UpdateTodoArgs.From(todo) with { DueDate = next };
            await BoardData.UpdateTodoAsync(todo.Id, args);
            await RefreshAsync();
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Could not reschedule this to-do. Try again.", Severity.Error);
        }
        finally
        {
            _busy.Remove(todo.Id);
        }
    }

    private async Task MoveTodoToTodayAsync(BoardItem todo) => await MoveTodoToDateAsync(todo, _today);

    private async Task MoveTodoToDateAsync(BoardItem todo, DateOnly date)
    {
        if (!_busy.Add(todo.Id))
        {
            return;
        }
        try
        {
            var args = UpdateTodoArgs.From(todo) with { DueDate = date };
            await BoardData.UpdateTodoAsync(todo.Id, args);
            await RefreshAsync();
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Could not reschedule this to-do. Try again.", Severity.Error);
        }
        finally
        {
            _busy.Remove(todo.Id);
        }
    }

    private async Task MoveOverdueToTodayAsync()
    {
        if (_bulkMoving || _overdue.Count == 0)
        {
            return;
        }
        var count = _overdue.Count;
        if (count > 3)
        {
            var confirmed = await Dialogs.ShowMessageBoxAsync(
                $"Move {count} overdue to-dos to today?",
                "All selected to-dos become due today. You can still move the to-dos back one by one.",
                "Move all",
                null,
                "Cancel");
            if (confirmed != true)
            {
                return;
            }
        }
        _bulkMoving = true;
        try
        {
            // One undo entry for the whole move: the data service registers an
            // undo per update, and the batch collapses them into a single
            // "Moved N overdue to-dos to today" snackbar with an Undo button.
            using (UndoService.BeginBatch($"Moved {count} overdue to-dos to today"))
            {
                foreach (var todo in _overdue)
                {
                    var args = UpdateTodoArgs.From(todo) with { DueDate = _today };
                    await BoardData.UpdateTodoAsync(todo.Id, args);
                }
            }
            await RefreshAsync();
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Could not move overdue to-dos. Try again.", Severity.Error);
        }
        finally
        {
            _bulkMoving = false;
        }
    }

    private async Task OpenTodoEditorAsync(BoardItem todo)
    {
        try
        {
            var parameters = new DialogParameters<EditTodoDialog> { { x => x.Item, todo } };
            var dialog = await Dialogs.ShowAsync<EditTodoDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
            await dialog.Result;
            await RefreshAsync();
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Could not open the to-do editor.", Severity.Error);
        }
    }

    private async Task OpenDailyEditorAsync(BoardItem daily)
    {
        try
        {
            var parameters = new DialogParameters<EditDailyDialog> { { x => x.Item, daily } };
            var dialog = await Dialogs.ShowAsync<EditDailyDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
            await dialog.Result;
            await RefreshAsync();
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Could not open the daily editor.", Severity.Error);
        }
    }

    private async Task AddTodoQuickAsync()
    {
        if (_addingTodo)
        {
            return;
        }
        var title = _newTodoTitle.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }
        _addingTodo = true;
        try
        {
            // Inline capture: the title is known upfront, so one create plus
            // the due-date patch produces exactly the row the user typed.
            // No editor round-trip, no junk row when abandoned.
            // One batch so a single undo reverts the whole add.
            using (UndoService.BeginBatch($"Added \"{title}\""))
            {
                var item = await BoardData.CreateItemAsync(BoardSection.Todo, title);
                if (item is not null)
                {
                    await BoardData.UpdateTodoAsync(item.Id, UpdateTodoArgs.From(item) with { DueDate = _selected });
                }
            }
            _newTodoTitle = string.Empty;
            await RefreshAsync();
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Could not add this to-do. Try again.", Severity.Error);
        }
        finally
        {
            _addingTodo = false;
        }
    }

    private string ViewBtnClass(CalView v) => _view == v ? "cal-seg__btn cal-seg__btn--active" : "cal-seg__btn";
    private string FilterBtnClass(UpcomingFilter f) => _filter == f ? "cal-seg__btn cal-seg__btn--active" : "cal-seg__btn";

    private string FormatDate(DateOnly date)
    {
        try
        {
            return DateFormatService.Format(date);
        }
        catch
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Nav.LocationChanged -= OnLocationChanged;
            _swipeRef?.Dispose();
            _swipeRef = null;
        }
    }
}
