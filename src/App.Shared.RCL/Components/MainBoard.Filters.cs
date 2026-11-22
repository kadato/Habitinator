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
    private List<BoardItem> _filteredHabits = [];
    private List<BoardItem> _filteredDailies = [];
    private List<BoardItem> _filteredTodos = [];
    private string _searchText = string.Empty;
    private readonly HashSet<string> _selectedFilterTags = [];
    private int _mobileSectionIndex;
    private ColumnSpec[] _cachedBoardColumns = [];
    private List<string> _cachedTagMenu = [];
    private string _cachedTagsTriggerTitle = "Filter by tags";
    private int _cachedDailiesDueOpenCount;
    private int _cachedOpenTodoCount;
    private CancellationTokenSource? _searchDebounceCts;
    private MudMenu? _tagsMenu;

    private List<BoardItem> FilteredHabits => _filteredHabits;
    private List<BoardItem> FilteredDailies => _filteredDailies;

    private int DailiesDueOpenCount => _cachedDailiesDueOpenCount;

    private List<BoardItem> FilteredTodos => _filteredTodos;

    private int OpenTodoCount => _cachedOpenTodoCount;

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

    private void RecomputeFilters()
    {
        _filteredHabits = FilterItems(Habits);
        _filteredDailies = FilterItems(Dailies);
        _filteredTodos = FilterItems(Todos);
        var today = UserDay.Today;
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
}
