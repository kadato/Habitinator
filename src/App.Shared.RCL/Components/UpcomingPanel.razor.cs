using System.Globalization;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class UpcomingPanel
{

    private enum UpcomingFilter { All, Dailies, Todos }
    private enum CalView { Day, Week, Next7, Month }

    private sealed record CalChip(string Title, string Hint, bool IsTodo, bool Done, bool IsRoutine);

    private bool _loading = true;
    private string? _error;
    private DateOnly _today;
    private BoardSnapshot? _snapshot;
    private IReadOnlyList<BoardItem> _overdue = [];
    private UpcomingFilter _filter = UpcomingFilter.All;
    private CalView _view = CalView.Month;
    private bool _bulkMoving;
    private readonly HashSet<Guid> _busy = [];
    private int _todayScheduled;
    private int _todayDone;
    private int _visibleYear;
    private int _visibleMonth;
    private DateOnly _selected;
    private bool _calInit;

    private bool ShowDailies => _filter != UpcomingFilter.Todos;
    private bool ShowTodos => _filter != UpcomingFilter.Dailies;
    private int OverdueCount => _overdue.Count;

    private int TodayRemaining
    {
        get
        {
            if (_snapshot is null)
            {
                return 0;
            }
            var count = 0;
            if (ShowDailies)
            {
                count += _snapshot.Dailies.Count(d => DailySchedule.IsDueOnDate(d, _today));
            }
            if (ShowTodos)
            {
                count += _snapshot.Todos.Count(t => !t.IsCompleted && t.TodoDueDate == _today);
            }
            return count;
        }
    }

    private int MonthTotal
    {
        get
        {
            if (_snapshot is null)
            {
                return 0;
            }
            var total = 0;
            var days = DateTime.DaysInMonth(_visibleYear, _visibleMonth);
            for (var d = 1; d <= days; d++)
            {
                var date = new DateOnly(_visibleYear, _visibleMonth, d);
                if (ShowDailies)
                {
                    total += _snapshot.Dailies.Count(x => DailySchedule.IsScheduledOn(x, date));
                }
                if (ShowTodos)
                {
                    total += _snapshot.Todos.Count(t => !t.IsCompleted && t.TodoDueDate == date);
                }
            }
            if (ShowTodos)
            {
                total += _overdue.Count;
            }
            return total;
        }
    }

    private int TodayPct => _todayScheduled == 0 ? 100 : (int)Math.Round(100.0 * _todayDone / _todayScheduled);

    private string MonthTitle => new DateOnly(_visibleYear, _visibleMonth, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    private string MonthShort => new DateOnly(_visibleYear, _visibleMonth, 1).ToString("MMM", CultureInfo.CurrentCulture);
    private DateOnly WeekStart => _selected.AddDays(-(((int)_selected.DayOfWeek + 6) % 7));
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
    private string HeaderTitle => _view switch
    {
        CalView.Day => SelectedLabel,
        CalView.Week => WeekRangeTitle,
        CalView.Next7 => SevenRangeTitle,
        _ => MonthTitle,
    };
    private string PrevLabel => _view switch
    {
        CalView.Day => "Previous day",
        CalView.Week => "Previous week",
        CalView.Next7 => "Previous 7 days",
        _ => "Previous month",
    };
    private string NextLabel => _view switch
    {
        CalView.Day => "Next day",
        CalView.Week => "Next week",
        CalView.Next7 => "Next 7 days",
        _ => "Next month",
    };
    private string SelectedAnchorId => $"upcoming-day-{_selected:yyyy-MM-dd}";

    private string HeadlineText
    {
        get
        {
            if (OverdueCount > 0 && TodayRemaining > 0)
            {
                return $"{OverdueCount} overdue, {TodayRemaining} due today";
            }
            if (OverdueCount > 0)
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

    private IReadOnlyList<BoardItem> SelectedDailies =>
        _snapshot is null || !ShowDailies
            ? []
            : [.. _snapshot.Dailies
                .Where(d => DailySchedule.IsScheduledOn(d, _selected))
                .OrderBy(d => d.Title, StringComparer.Ordinal)];

    private IReadOnlyList<BoardItem> SelectedTodos =>
        _snapshot is null || !ShowTodos
            ? []
            : [.. _snapshot.Todos
                .Where(t => !t.IsCompleted && t.TodoDueDate == _selected)
                .OrderBy(t => t.Title, StringComparer.Ordinal)];

    private int SelectedVisibleCount => SelectedDailies.Count + SelectedTodos.Count;

    private string SelectedLabel => DayLabel(_selected);

    protected override async Task OnInitializedAsync()
    {
        await LoadPersistedViewStateAsync();
        await LoadAsync();
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
        _todayScheduled = snapshot.Dailies.Count(d => DailySchedule.IsScheduledOn(d, _today));
        var todayDue = snapshot.Dailies.Count(d => DailySchedule.IsDueOnDate(d, _today));
        _todayDone = Math.Max(0, _todayScheduled - todayDue);
        if (!_calInit)
        {
            _visibleYear = _today.Year;
            _visibleMonth = _today.Month;
            _selected = _today;
            _calInit = true;
        }
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

    private void SetFilter(UpcomingFilter filter)
    {
        _filter = filter;
        _ = PersistViewStateAsync();
    }

    private void SetView(CalView view)
    {
        _view = view;
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
    }

    private void NextMonth()
    {
        var first = new DateOnly(_visibleYear, _visibleMonth, 1).AddMonths(1);
        _visibleYear = first.Year;
        _visibleMonth = first.Month;
    }

    private void GoToday()
    {
        _visibleYear = _today.Year;
        _visibleMonth = _today.Month;
        _selected = _today;
    }

    private static string TodoCompleteLabel(BoardItem todo) => $"Mark {todo.Title} complete";

    private bool IsBusy(Guid id) => _busy.Contains(id);

    private List<DateOnly> GridCells()
    {
        var first = new DateOnly(_visibleYear, _visibleMonth, 1);
        var lead = ((int)first.DayOfWeek + 6) % 7;
        var start = first.AddDays(-lead);
        return Enumerable.Range(0, 42).Select(start.AddDays).ToList();
    }

    private List<DateOnly> WeekCells() => Enumerable.Range(0, 7).Select(WeekStart.AddDays).ToList();

    private List<DateOnly> Next7Cells() => Enumerable.Range(0, 7).Select(_selected.AddDays).ToList();

    private List<DateOnly> DayStripCells() => Enumerable.Range(-3, 7).Select(_selected.AddDays).ToList();

    private List<DateOnly> RangeCells() => _view == CalView.Next7 ? Next7Cells() : WeekCells();

    private static string RangeTitle(DateOnly start, DateOnly end) =>
        start.Year != end.Year
            ? $"{start:MMM d, yyyy} - {end:MMM d, yyyy}"
            : $"{start:MMM d} - {end:MMM d}, {end:yyyy}";

    private string SevenRangeTitle => RangeTitle(_selected, _selected.AddDays(6));

    private string GridAriaLabel => _view == CalView.Next7
        ? $"Next 7 days {SevenRangeTitle} calendar"
        : $"Week of {WeekRangeTitle} calendar";

    private IReadOnlyList<BoardItem> CellDailies(DateOnly date) =>
        _snapshot is null || !ShowDailies
            ? []
            : [.. _snapshot.Dailies
                .Where(d => DailySchedule.IsScheduledOn(d, date))
                .OrderBy(d => d.Title, StringComparer.Ordinal)];

    private IReadOnlyList<BoardItem> CellTodos(DateOnly date) =>
        _snapshot is null || !ShowTodos
            ? []
            : [.. _snapshot.Todos
                .Where(t => !t.IsCompleted && t.TodoDueDate == date)
                .OrderBy(t => t.Title, StringComparer.Ordinal)];

    private static string DailyHint(BoardItem d) => $"{d.Title} ({UpcomingSchedule.DescribeDaily(d)})";

    private List<CalChip> CellChips(IReadOnlyList<BoardItem> dailies, IReadOnlyList<BoardItem> todos)
    {
        List<CalChip> chips = [];
        chips.AddRange(dailies.Select(d => new CalChip(d.Title, DailyHint(d), false, d.DailyLastCompletedOn == _today, false)));
        chips.AddRange(todos.Select(t => new CalChip(t.Title, t.TodoDueDate is { } due ? $"{t.Title} ({TodoDueRelativeText.Format(due, _today)})" : t.Title, true, false, false)));
        return chips;
    }

    /// <summary>
    /// Month cells collapse repeating dailies into one routine chip so the same
    /// habits do not repeat on every day. Week cells keep every item.
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
        chips.AddRange(todos.Select(t => new CalChip(t.Title, t.TodoDueDate is { } due ? $"{t.Title} ({TodoDueRelativeText.Format(due, _today)})" : t.Title, true, false, false)));
        return chips;
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
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            cls += " cal-cell--weekend";
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

    private string CellAria(DateOnly date, int total) => $"{DayLabel(date)}, {FormatDate(date)}, {total} items";

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
        _bulkMoving = true;
        try
        {
            foreach (var todo in _overdue)
            {
                var args = UpdateTodoArgs.From(todo) with { DueDate = _today };
                await BoardData.UpdateTodoAsync(todo.Id, args);
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

    private string FilterBtnClass(UpcomingFilter f) => _filter == f ? "cal-seg__btn cal-seg__btn--active" : "cal-seg__btn";
    private string ViewBtnClass(CalView v) => _view == v ? "cal-seg__btn cal-seg__btn--active" : "cal-seg__btn";

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
}
