#pragma warning disable S8949, S6966, IDE0028, IDE0042, CA1859
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class BoardColumn : IAsyncDisposable
{
    [Inject] public required IBoardDataService BoardData { get; set; }
    [Inject] public required IDialogService DialogService { get; set; }
    [Inject] public required IUserNotifier Notifier { get; set; }
    [Inject] public required IUserTimeZoneService TimeZoneService { get; set; }
    [Inject] public required IUserDayService UserDay { get; set; }
    [Inject] public required IUndoService UndoService { get; set; }
    [Inject] public required IJSRuntime JS { get; set; }
    [Inject] public required IBoardColumnStateStore ColumnState { get; set; }
    [Inject] public required GlobalTimerService TimerService { get; set; }

    [Parameter][EditorRequired] public BoardSection Section { get; set; }

    [Parameter] public IReadOnlyList<BoardItem> Items { get; set; } = [];

    [Parameter] public EventCallback OnChanged { get; set; }

    [Parameter] public bool IsBoardFilterExcludingAll { get; set; }

    [Parameter] public string? SearchQuery { get; set; }

    [Parameter] public EventCallback OnClearBoardFilter { get; set; }

    private HabitListFilter _habitFilter;
    private DailyListFilter _dailyFilter = DailyListFilter.Due;
    private TodoListFilter _todoFilter = TodoListFilter.Active;

    protected override async Task OnInitializedAsync()
    {
        await UserDay.InitializeAsync();
        UserDay.DayChanged += OnUserDayChanged;
        TimerService.StateChanged += OnTimerStateChanged;
        await LoadPersistedStateAsync();
    }

    private void OnTimerStateChanged()
    {
        _ = InvokeAsync(() =>
        {
            InvalidateVisibleCache();
            StateHasChanged();
        });
    }

    private void OnUserDayChanged(object? sender, EventArgs e)
    {
        _ = InvokeAsync(() =>
        {
            _needRefresh = true;
            InvalidateVisibleCache();
            StateHasChanged();
        });
    }

    private async Task LoadPersistedStateAsync()
    {
        BoardColumnFilterState? state;
        try
        {
            state = await ColumnState.GetAsync();
        }
        catch (Exception)
        {
            return;
        }

        if (state is null)
        {
            return;
        }

        if (Enum.TryParse<HabitListFilter>(state.HabitFilter, ignoreCase: true, out var habit))
        {
            _habitFilter = habit;
        }

        if (Enum.TryParse<DailyListFilter>(state.DailyFilter, ignoreCase: true, out var daily))
        {
            _dailyFilter = daily;
        }

        if (Enum.TryParse<TodoListFilter>(state.TodoFilter, ignoreCase: true, out var todo))
        {
            _todoFilter = todo;
        }
    }

    private async Task PersistStateAsync()
    {
        try
        {
            await ColumnState.SetAsync(new BoardColumnFilterState(
                _habitFilter.ToString(),
                _dailyFilter.ToString(),
                _todoFilter.ToString()));
        }
        catch (Exception)
        {
            // Ignored. Filters do not persist.
        }
    }

    private void SetHabitFilter(HabitListFilter filter)
    {
        _habitFilter = filter;
        _needRefresh = true;
        InvalidateVisibleCache();
        _ = PersistStateAsync();
    }

    private void SetDailyFilter(DailyListFilter filter)
    {
        _dailyFilter = filter;
        _needRefresh = true;
        InvalidateVisibleCache();
        _ = PersistStateAsync();
    }

    private void SetTodoFilter(TodoListFilter filter)
    {
        _todoFilter = filter;
        _needRefresh = true;
        InvalidateVisibleCache();
        _ = PersistStateAsync();
    }

    private bool IsColumnFilterHidingAll =>
        !IsBoardFilterExcludingAll && EffectiveItems.Any() && VisibleItems().Count == 0;

    private string GetColumnFilterName() => Section switch
    {
        BoardSection.Habit => _habitFilter switch
        {
            HabitListFilter.Weak => "New",
            HabitListFilter.Strong => "Established",
            _ => "All"
        },
        BoardSection.Daily => _dailyFilter switch
        {
            DailyListFilter.Due => "Due now",
            DailyListFilter.NotDue => "Later",
            _ => "All"
        },
        _ => _todoFilter.ToString()
    };

    private string GetResetFilterLabel() => Section switch
    {
        BoardSection.Habit => "Show all habits",
        BoardSection.Daily => "Show all dailies",
        _ => "Show all to-dos"
    };

    private string? GetFilterHint() => Section switch
    {
        BoardSection.Habit => _habitFilter switch
        {
            HabitListFilter.Weak => "Fewer than 3 logs.",
            HabitListFilter.Strong => "3 or more logs.",
            _ => null
        },
        BoardSection.Daily => _dailyFilter switch
        {
            DailyListFilter.NotDue => "Not due today.",
            _ => null
        },
        BoardSection.Todo => _todoFilter switch
        {
            TodoListFilter.Scheduled => "With a due date.",
            TodoListFilter.Done => "Done.",
            _ => null
        },
        _ => null
    };

    private void ResetColumnFilter()
    {
        switch (Section)
        {
            case BoardSection.Habit:
                SetHabitFilter(HabitListFilter.All);
                break;
            case BoardSection.Daily:
                SetDailyFilter(DailyListFilter.All);
                break;
            default:
                SetTodoFilter(TodoListFilter.Active);
                break;
        }
    }

    private enum HabitListFilter
    {
        All,
        Weak,
        Strong
    }

    private enum DailyListFilter
    {
        All,
        Due,
        NotDue
    }

    private enum TodoListFilter
    {
        Active,
        Scheduled,
        Done
    }

    private static string GetFilterBtnClass(bool active) =>
        active ? "board-filter-btn board-filter-btn--active" : "board-filter-btn";

    private string GetTitle()
    {
        return Section switch
        {
            BoardSection.Habit => "Habits",
            BoardSection.Daily => "Dailies",
            BoardSection.Todo => "To-dos",
            _ => "Board"
        };
    }

    private string GetAddPlaceholder()
    {
        return Section switch
        {
            BoardSection.Habit => "Add habit",
            BoardSection.Daily => "Add daily",
            BoardSection.Todo => "Add todo",
            _ => "Add"
        };
    }



    private string GetEmptyStateIcon()
    {
        return Section switch
        {
            BoardSection.Habit => Icons.Material.Outlined.Bolt,
            BoardSection.Daily => Icons.Material.Outlined.CalendarToday,
            BoardSection.Todo => Icons.Material.Outlined.PlaylistAddCheck,
            _ => Icons.Material.Outlined.Info
        };
    }

    private string GetEmptyStateTitle()
    {
        return Section switch
        {
            BoardSection.Habit => "No habits yet",
            BoardSection.Daily => "No dailies yet",
            BoardSection.Todo => "No to-dos yet",
            _ => "No items here yet"
        };
    }

    private string GetEmptyStateDesc()
    {
        return Section switch
        {
            BoardSection.Habit => "Add a habit to begin.",
            BoardSection.Daily => "Add a daily to begin.",
            BoardSection.Todo => "Add a to-do to begin.",
            _ => "Add an item above to start."
        };
    }

    private string GetGlossaryText()
    {
        return Section switch
        {
            BoardSection.Habit => "Long-term goals. Log with + or -.",
            BoardSection.Daily => "Repeats on schedule. Check off each period.",
            BoardSection.Todo => "One-time tasks with due dates.",
            _ => string.Empty
        };
    }

    private static string GetFooterText()
    {
        return string.Empty;
    }

    private string GetAccentClass()
    {
        return Section switch
        {
            BoardSection.Habit => "board-column--habit",
            BoardSection.Daily => "board-column--daily",
            BoardSection.Todo => "board-column--todo",
            _ => string.Empty
        };
    }

    public async ValueTask DisposeAsync()
    {
        UserDay.DayChanged -= OnUserDayChanged;
        TimerService.StateChanged -= OnTimerStateChanged;
        try
        {
            await JS.InvokeVoidAsync("HabitinatorSortable.destroy", _columnInstanceId);
        }
        catch (JSDisconnectedException)
        {
            // Ignored during page navigation or hot reload
        }
        catch (JSException)
        {
            // Ignored during page navigation or hot reload
        }
        catch (TaskCanceledException)
        {
            // Ignored during page navigation or hot reload
        }
        catch (InvalidOperationException)
        {
            // Ignored during page navigation or hot reload
        }
        finally
        {
            _selfRef?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private DateOnly BoardToday() => UserDay.Today;
}
