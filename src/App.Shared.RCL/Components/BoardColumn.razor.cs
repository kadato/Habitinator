using App.Shared.RCL.Components.Dialogs;
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
    [Inject] public required IUserDateFormatService DateFormatService { get; set; }
    [Inject] public required IUndoService UndoService { get; set; }
    [Inject] public required IJSRuntime JS { get; set; }
    [Inject] public required IBoardColumnStateStore ColumnState { get; set; }

    [Parameter][EditorRequired] public BoardSection Section { get; set; }

    [Parameter] public IReadOnlyList<BoardItem> Items { get; set; } = [];

    [Parameter] public EventCallback OnChanged { get; set; }

    [Parameter] public bool IsBoardFilterExcludingAll { get; set; }

    private string _draft = string.Empty;

    private HabitListFilter _habitFilter;
    private DailyListFilter _dailyFilter = DailyListFilter.Due;
    private TodoListFilter _todoFilter = TodoListFilter.Active;

    protected override async Task OnInitializedAsync()
    {
        await LoadPersistedStateAsync();
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
            // Ignored, filters simply won't persist
        }
    }

    private void SetHabitFilter(HabitListFilter filter)
    {
        _habitFilter = filter;
        _needRefresh = true;
        _ = PersistStateAsync();
    }

    private void SetDailyFilter(DailyListFilter filter)
    {
        _dailyFilter = filter;
        _needRefresh = true;
        _ = PersistStateAsync();
    }

    private void SetTodoFilter(TodoListFilter filter)
    {
        _todoFilter = filter;
        _needRefresh = true;
        _ = PersistStateAsync();
    }

    private readonly Dictionary<Guid, double> _sortOrderOverrides = [];
    private readonly Dictionary<Guid, BoardItem> _optimisticOverrides = [];
    private readonly HashSet<Guid> _optimisticDeletions = [];
    private readonly Dictionary<Guid, BoardItem> _optimisticCreations = [];

    private IEnumerable<BoardItem> EffectiveItems
    {
        get
        {
            var baseItems = Items
                .Where(x => !_optimisticDeletions.Contains(x.Id))
                .Select(x => _optimisticOverrides.TryGetValue(x.Id, out var o) ? o : x);
            var serverIds = new HashSet<Guid>([.. Items.Select(x => x.Id)]);
            var creations = _optimisticCreations.Values.Where(x => !serverIds.Contains(x.Id));
            return baseItems.Concat(creations);
        }
    }

    private ElementReference _containerRef;
    private DotNetObjectReference<BoardColumn>? _selfRef;
    private bool _needRefresh;
    private IReadOnlyList<BoardItem>? _lastItemsForRefresh;
    private int _lastItemsCount = -1;

    protected override void OnParametersSet()
    {
        var optimisticStateCount = CountOptimisticState();
        PruneSortOrderOverrides();
        PruneOptimisticOverrides();
        PruneOptimisticDeletions();
        PruneOptimisticCreations();

        // Re-init the sortable only when the item list or the optimistic state actually changed.
        var itemsChanged = !ReferenceEquals(_lastItemsForRefresh, Items) || Items.Count != _lastItemsCount;
        var optimisticChanged = optimisticStateCount != CountOptimisticState();
        if (itemsChanged || optimisticChanged)
        {
            _lastItemsForRefresh = Items;
            _lastItemsCount = Items.Count;
            _needRefresh = true;
        }
    }

    private int CountOptimisticState() =>
        _sortOrderOverrides.Count + _optimisticOverrides.Count + _optimisticDeletions.Count + _optimisticCreations.Count;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsItemDraggable() && VisibleItems().Count > 0 && (firstRender || _needRefresh))
        {
            _needRefresh = false;
            await InitSortableAsync();
        }
    }

    private async Task InitSortableAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("habitinatorLoadScript", "_content/App.Shared.RCL/js/sortable.min.js");
            await JS.InvokeVoidAsync("habitinatorLoadScript", "_content/App.Shared.RCL/js/boardSortable.js");
            _selfRef ??= DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("HabitinatorSortable.init", Section.ToString(), _containerRef, _selfRef);
        }
        catch (JSDisconnectedException)
        {
            // Ignored during page navigation or hot reload
        }
        catch (TaskCanceledException)
        {
            // Ignored during page navigation or hot reload
        }
    }

    private void PruneSortOrderOverrides()
    {
        foreach (var id in _sortOrderOverrides.Keys.ToList())
        {
            var item = Items.FirstOrDefault(x => x.Id == id);
            if (item is null)
            {
                _sortOrderOverrides.Remove(id);
                continue;
            }

            if (item.SortOrder is { } serverOrder
                && Math.Abs(serverOrder - _sortOrderOverrides[id]) < 0.0001)
            {
                _sortOrderOverrides.Remove(id);
            }
        }
    }

    private void PruneOptimisticOverrides()
    {
        foreach (var id in _optimisticOverrides.Keys.ToList())
        {
            var serverItem = Items.FirstOrDefault(x => x.Id == id);
            if (serverItem is null)
            {
                _optimisticOverrides.Remove(id);
                continue;
            }

            var overrideItem = _optimisticOverrides[id];
            var match = serverItem.IsCompleted == overrideItem.IsCompleted
                && serverItem.Title == overrideItem.Title
                && serverItem.Notes == overrideItem.Notes
                && serverItem.Tags == overrideItem.Tags
                && serverItem.ChecklistJson == overrideItem.ChecklistJson;

            if (Section == BoardSection.Habit)
            {
                match = match
                    && serverItem.Counter == overrideItem.Counter
                    && serverItem.NegativeCounter == overrideItem.NegativeCounter
                    && serverItem.TrackPlus == overrideItem.TrackPlus
                    && serverItem.TrackMinus == overrideItem.TrackMinus
                    && serverItem.ResetPeriod == overrideItem.ResetPeriod;
            }
            else if (Section == BoardSection.Daily)
            {
                match = match
                    && serverItem.DailyStartDate == overrideItem.DailyStartDate
                    && serverItem.DailyRepeat == overrideItem.DailyRepeat
                    && serverItem.DailyRepeatInterval == overrideItem.DailyRepeatInterval;
                // Note: We intentionally do NOT check Counter (streak) here because the server
                // automatically increments/updates it upon completion, which would prevent
                // the optimistic override from being pruned.
            }
            else if (Section == BoardSection.Todo)
            {
                match = match
                    && serverItem.TodoDueDate == overrideItem.TodoDueDate
                    && serverItem.TodoRepeatIntervalDays == overrideItem.TodoRepeatIntervalDays;
            }

            if (match)
            {
                _optimisticOverrides.Remove(id);
            }
        }
    }

    private void PruneOptimisticDeletions()
    {
        foreach (var id in _optimisticDeletions.ToList())
        {
            var serverItem = Items.FirstOrDefault(x => x.Id == id);
            if (serverItem is null)
            {
                _optimisticDeletions.Remove(id);
            }
        }
    }

    private void PruneOptimisticCreations()
    {
        foreach (var id in _optimisticCreations.Keys.Where(id => Items.Any(x => x.Id == id)).ToList())
        {
            _optimisticCreations.Remove(id);
        }
    }

    private double GetInitialSortOrderForOptimisticCreation()
    {
        var current = VisibleItems();
        if (current.Count == 0)
        {
            return 1.0;
        }
        var max = current.Max(x => GetEffectiveSortOrder(x) ?? 0.0);
        return max + 1.0;
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

    private IReadOnlyList<BoardItem> VisibleItems()
    {
        return Section switch
        {
            BoardSection.Habit => OrderBySort(FilterHabits()),
            BoardSection.Daily => OrderBySort(FilterDailies()),
            BoardSection.Todo => OrderTodos(FilterTodos()),
            _ => OrderBySort(EffectiveItems)
        };
    }

    private double? GetEffectiveSortOrder(BoardItem item) =>
        _sortOrderOverrides.TryGetValue(item.Id, out var order) ? order : item.SortOrder;

    private List<BoardItem> OrderBySort(IEnumerable<BoardItem> items) =>
        [.. items.OrderBy(x => GetEffectiveSortOrder(x) ?? double.MaxValue).ThenBy(x => x.Id)];

    private List<BoardItem> FilterHabits()
    {
        return _habitFilter switch
        {
            HabitListFilter.Weak => [.. EffectiveItems.Where(x => x.Counter < 3)],
            HabitListFilter.Strong => [.. EffectiveItems.Where(x => x.Counter >= 3)],
            _ => [.. EffectiveItems]
        };
    }

    private List<BoardItem> FilterDailies()
    {
        var today = DailySchedule.LocalToday(TimeZoneService);
        return _dailyFilter switch
        {
            DailyListFilter.Due => [.. EffectiveItems.Where(d => DailySchedule.IsDueOnDate(d, today))],
            DailyListFilter.NotDue => [.. EffectiveItems.Where(d => !DailySchedule.IsDueOnDate(d, today))],
            _ => [.. EffectiveItems]
        };
    }

    private List<BoardItem> FilterTodos()
    {
        return _todoFilter switch
        {
            TodoListFilter.Active => [.. EffectiveItems.Where(x => !x.IsCompleted)],
            TodoListFilter.Scheduled => [.. EffectiveItems.Where(x => !x.IsCompleted && x.TodoDueDate is not null)],
            TodoListFilter.Done => [.. EffectiveItems.Where(x => x.IsCompleted)],
            _ => [.. EffectiveItems]
        };
    }

    private IReadOnlyList<BoardItem> OrderTodos(IReadOnlyList<BoardItem> items) =>
        _todoFilter switch
        {
            TodoListFilter.Active => TodoOrdering.OrderForActiveTab(items, GetEffectiveSortOrder),
            TodoListFilter.Scheduled => TodoOrdering.OrderForScheduledTab(items),
            _ => OrderBySort(items)
        };

    private bool AnyDoneTodos => EffectiveItems.Any(x => x.IsCompleted);

    private bool IsItemDraggable() =>
        Section != BoardSection.Todo || _todoFilter == TodoListFilter.Active;

    private string GetTitle()
    {
        return Section switch
        {
            BoardSection.Habit => "Habits",
            BoardSection.Daily => "Dailies",
            BoardSection.Todo => "To Do's",
            _ => "Board"
        };
    }

    private string GetAddPlaceholder()
    {
        return Section switch
        {
            BoardSection.Habit => "Add a Habit",
            BoardSection.Daily => "Add a Daily",
            BoardSection.Todo => "Add a To Do",
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
            BoardSection.Habit => "Add long-term habits to build or break. Log progress with + or -.",
            BoardSection.Daily => "Add recurring routines. Check them off when done for the day.",
            BoardSection.Todo => "Add single tasks. Set deadlines and checklists.",
            _ => "Add an item above to start."
        };
    }

    private string GetGlossaryText()
    {
        return Section switch
        {
            BoardSection.Habit => "Habits track long-term goals without a final done state. Use counters to log progress.",
            BoardSection.Daily => "Dailies repeat on set schedules like daily or weekly. They reset at midnight. Check them off each period to keep your streak.",
            BoardSection.Todo => "To-Dos are single tasks with due dates, checklists, and notes.",
            _ => string.Empty
        };
    }

    private string GetFooterText()
    {
        return Section switch
        {
            BoardSection.Habit => "Habits track long-term goals. Use + or - to log entries. Click title for notes.",
            BoardSection.Daily => "Dailies repeat automatically. Check off when done for today. Click title for details.",
            BoardSection.Todo => "To-Dos are single tasks. On Active, drag cards to reorder. Click title for details.",
            _ => string.Empty
        };
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

    // -- Reordering (SortableJS) --

    [JSInvokable]
    public async Task OnJsReorderAsync(int oldIndex, int newIndex)
    {
        if (!IsItemDraggable())
        {
            return;
        }

        List<BoardItem> visible = [.. VisibleItems()];
        if (oldIndex < 0 || oldIndex >= visible.Count || newIndex < 0 || newIndex >= visible.Count || oldIndex == newIndex)
        {
            return;
        }

        var sourceItem = visible[oldIndex];
        List<BoardItem> reordered = [.. visible];
        reordered.RemoveAt(oldIndex);
        reordered.Insert(newIndex, sourceItem);

        var newSortOrder = Section == BoardSection.Todo && _todoFilter == TodoListFilter.Active
            ? TodoOrdering.ComputeMidpointSortOrderForActiveTab(reordered, newIndex, GetEffectiveSortOrder)
            : BoardItemReorder.ComputeMidpointSortOrder(reordered, newIndex, GetEffectiveSortOrder);

        if (newSortOrder is not { } sortOrder)
        {
            _needRefresh = true;
            StateHasChanged();
            return;
        }

        await ApplySortOrderAsync(sourceItem.Id, sortOrder, () => PersistReorderAsync(sourceItem, sortOrder));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (IsItemDraggable())
            {
                await JS.InvokeVoidAsync("HabitinatorSortable.destroy", Section.ToString());
            }
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

    private Task PersistReorderAsync(BoardItem item, double newSortOrder)
    {
        return Section switch
        {
            BoardSection.Habit => BoardData.UpdateHabitAsync(
                item.Id,
                new UpdateHabitArgs(
                    item.Title,
                    item.Notes,
                    item.Tags,
                    item.TrackPlus,
                    item.TrackMinus,
                    item.ResetPeriod,
                    item.Counter,
                    item.NegativeCounter,
                    item.ChecklistJson,
                    SortOrder: newSortOrder)),

            BoardSection.Daily => BoardData.UpdateDailyAsync(
                item.Id,
                new UpdateDailyArgs(
                    item.Title,
                    item.Notes,
                    item.Tags,
                    item.DailyStartDate,
                    item.DailyRepeat,
                    item.DailyRepeatInterval,
                    item.ChecklistJson,
                    item.Counter,
                    SortOrder: newSortOrder)),

            BoardSection.Todo => BoardData.UpdateTodoAsync(
                item.Id,
                new UpdateTodoArgs(
                    item.Title,
                    item.Notes,
                    item.Tags,
                    item.ChecklistJson,
                    item.TodoDueDate,
                    SortOrder: newSortOrder,
                    TodoRepeatIntervalDays: item.TodoRepeatIntervalDays)),

            _ => Task.CompletedTask
        };
    }

    private DateOnly BoardToday() => DailySchedule.LocalToday(TimeZoneService);

    private async Task SetChecklistItemDoneAsync(BoardItem item, Guid lineId, bool done)
    {
        var rows = DailyChecklistJson.Parse(item.ChecklistJson);
        List<DailyChecklistItem> list = [.. rows];
        var i = list.FindIndex(x => x.Id == lineId);
        if (i < 0 || list[i].IsDone == done)
        {
            return;
        }

        list[i] = list[i] with { IsDone = done };
        var json = DailyChecklistJson.Serialize(list);
        if (json is null)
        {
            return;
        }

        var optimistic = item with { ChecklistJson = json };
        Task mutation;
        if (Section == BoardSection.Daily)
        {
            mutation = BoardData.UpdateDailyAsync(
                item.Id,
                new UpdateDailyArgs(
                    item.Title,
                    item.Notes,
                    item.Tags,
                    item.DailyStartDate,
                    item.DailyRepeat,
                    item.DailyRepeatInterval,
                    json,
                    item.Counter));
        }
        else if (Section == BoardSection.Todo)
        {
            mutation = BoardData.UpdateTodoAsync(
                item.Id,
                new UpdateTodoArgs(
                    item.Title,
                    item.Notes,
                    item.Tags,
                    json,
                    item.TodoDueDate,
                    TodoRepeatIntervalDays: item.TodoRepeatIntervalDays));
        }
        else
        {
            mutation = BoardData.UpdateHabitAsync(
                item.Id,
                new UpdateHabitArgs(
                    item.Title,
                    item.Notes,
                    item.Tags,
                    item.TrackPlus,
                    item.TrackMinus,
                    item.ResetPeriod,
                    item.Counter,
                    item.NegativeCounter,
                    json));
        }

        await ApplyOverrideAsync(item.Id, optimistic, () => mutation);
    }

    private async Task OnDraftKeyAsync(KeyboardEventArgs e)
    {
        if (e.Key is not "Enter" and not "NumpadEnter")
        {
            return;
        }

        await AddFromDraftAsync();
    }

    private async Task AddFromDraftAsync()
    {
        if (string.IsNullOrWhiteSpace(_draft))
        {
            return;
        }

        var title = _draft.Trim();
        var newId = Guid.NewGuid();
        BoardItem tempItem = new(
            Id: newId,
            Title: title,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            SortOrder: GetInitialSortOrderForOptimisticCreation()
        );
        _draft = string.Empty;

        await ApplyCreationAsync(newId, tempItem, () => BoardData.CreateItemAsync(Section, title, newId));
    }

    private Task HabitUpAsync(BoardItem item)
    {
        var optimistic = item with { Counter = item.Counter + 1 };
        return ApplyOverrideAsync(item.Id, optimistic, () => BoardData.IncrementHabitPlusAsync(item.Id));
    }

    private Task HabitDownAsync(BoardItem item)
    {
        var optimistic = item with { NegativeCounter = item.NegativeCounter + 1 };
        return ApplyOverrideAsync(item.Id, optimistic, () => BoardData.IncrementHabitMinusAsync(item.Id));
    }

    private Task ToggleAsync(BoardItem item)
    {
        if (Section == BoardSection.Todo && !item.IsCompleted && item.TodoRepeatIntervalDays is > 0)
        {
            return ToggleRecurringTodoAsync(item);
        }

        var nextCompleted = !item.IsCompleted;
        DateOnly? lastCompleted = nextCompleted ? BoardToday() : null;
        var optimistic = item with { IsCompleted = nextCompleted, DailyLastCompletedOn = lastCompleted };
        return ApplyOverrideAsync(item.Id, optimistic, () => BoardData.ToggleItemAsync(Section, item.Id));
    }

    private Task ToggleRecurringTodoAsync(BoardItem item)
    {
        var interval = item.TodoRepeatIntervalDays!.Value;
        var nextDue = (item.TodoDueDate ?? BoardToday()).AddDays(interval);
        while (nextDue <= BoardToday())
        {
            nextDue = nextDue.AddDays(interval);
        }

        var optimistic = item with { IsCompleted = true, TodoDueDate = nextDue, DailyLastCompletedOn = BoardToday() };
        return ApplyOverrideAsync(item.Id, optimistic, async () =>
        {
            await BoardData.UpdateTodoAsync(
                item.Id,
                new UpdateTodoArgs(
                    item.Title,
                    item.Notes,
                    item.Tags,
                    item.ChecklistJson,
                    nextDue,
                    SortOrder: item.SortOrder,
                    TodoRepeatIntervalDays: item.TodoRepeatIntervalDays));
            await BoardData.ToggleItemAsync(BoardSection.Todo, item.Id);
            await Notifier.NotifyAsync(
                $"Repeating to-do. Next occurrence {DateFormatService.Format(nextDue)}.",
                Severity.Info);
        });
    }

    private async Task OpenEditHabitAsync(BoardItem item)
    {
        DialogParameters<EditHabitDialog> parameters = new() { { x => x.Item, item } };
        var dialog = await DialogService.ShowAsync<EditHabitDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: EditHabitDialogResult r })
        {
            switch (r.Action)
            {
                case EditDialogAction.Archive:
                    await ArchiveHabitAsync(item);
                    break;
                case EditDialogAction.Delete:
                    await DeleteHabitAsync(item);
                    break;
            }
        }
    }

    private Task ArchiveHabitAsync(BoardItem item) =>
        ApplyDeletionAsync(item.Id, () => BoardData.ArchiveItemAsync(BoardSection.Habit, item.Id));

    private Task DeleteHabitAsync(BoardItem item) =>
        ApplyDeletionAsync(item.Id, () => BoardData.DeleteItemAsync(BoardSection.Habit, item.Id));

    private async Task OpenEditDailyAsync(BoardItem item)
    {
        DialogParameters<EditDailyDialog> parameters = new() { { x => x.Item, item } };
        var dialog = await DialogService.ShowAsync<EditDailyDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: EditDailyDialogResult r })
        {
            switch (r.Action)
            {
                case EditDialogAction.Archive:
                    await ArchiveDailyAsync(item);
                    break;
                case EditDialogAction.Delete:
                    await DeleteDailyAsync(item);
                    break;
            }
        }
    }

    private Task ArchiveDailyAsync(BoardItem item) =>
        ApplyDeletionAsync(item.Id, () => BoardData.ArchiveItemAsync(BoardSection.Daily, item.Id));

    private Task DeleteDailyAsync(BoardItem item) =>
        ApplyDeletionAsync(item.Id, () => BoardData.DeleteItemAsync(BoardSection.Daily, item.Id));

    private async Task OpenEditTodoAsync(BoardItem item)
    {
        DialogParameters<EditTodoDialog> parameters = new() { { x => x.Item, item } };
        var dialog = await DialogService.ShowAsync<EditTodoDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: EditTodoDialogResult r })
        {
            switch (r.Action)
            {
                case EditDialogAction.Archive:
                    await ArchiveTodoAsync(item);
                    break;
                case EditDialogAction.Delete:
                    await DeleteTodoAsync(item);
                    break;
            }
        }
    }

    private Task ArchiveTodoAsync(BoardItem item) =>
        ApplyDeletionAsync(item.Id, () => BoardData.ArchiveItemAsync(BoardSection.Todo, item.Id));

    private Task DeleteTodoAsync(BoardItem item) =>
        ApplyDeletionAsync(item.Id, () => BoardData.DeleteItemAsync(BoardSection.Todo, item.Id));

    private Task OpenItemEditorAsync(BoardItem item)
    {
        return Section switch
        {
            BoardSection.Habit => OpenEditHabitAsync(item),
            BoardSection.Daily => OpenEditDailyAsync(item),
            BoardSection.Todo => OpenEditTodoAsync(item),
            _ => Task.CompletedTask
        };
    }

    private Task DeleteAsync(Guid id) =>
        ApplyDeletionAsync(id, () => BoardData.DeleteItemAsync(Section, id));

    private Task MoveToTopAsync(BoardItem item) => MoveToIndexAsync(item, 0);

    private Task MoveToBottomAsync(BoardItem item) => MoveToIndexAsync(item, VisibleItems().Count - 1);

    private async Task MoveToIndexAsync(BoardItem item, int targetIndex)
    {
        List<BoardItem> visible = [.. VisibleItems()];
        var sourceIndex = visible.FindIndex(x => x.Id == item.Id);
        if (sourceIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        List<BoardItem> reordered = [.. visible];
        reordered.RemoveAt(sourceIndex);
        reordered.Insert(targetIndex, item);

        var newSortOrder = Section == BoardSection.Todo && _todoFilter == TodoListFilter.Active
            ? TodoOrdering.ComputeMidpointSortOrderForActiveTab(reordered, targetIndex, GetEffectiveSortOrder)
            : BoardItemReorder.ComputeMidpointSortOrder(reordered, targetIndex, GetEffectiveSortOrder);

        if (newSortOrder is not { } sortOrder)
        {
            return;
        }

        await ApplySortOrderAsync(item.Id, sortOrder, () => PersistReorderAsync(item, sortOrder));
    }

    private async Task DeleteAllDoneTodosAsync()
    {
        if (Section != BoardSection.Todo)
        {
            return;
        }

        List<BoardItem> toDelete = [.. EffectiveItems.Where(x => x.IsCompleted)];
        if (toDelete.Count == 0)
        {
            return;
        }

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Delete all done to-dos?",
            $"This will permanently remove {toDelete.Count} done to-do(s). This cannot be undone.",
            "Delete all",
            null,
            "Cancel");

        if (confirmed != true)
        {
            return;
        }

        var deleted = 0;
        try
        {
            using (UndoService.BeginBatch($"Delete {toDelete.Count} done to-dos"))
            {
                foreach (var item in toDelete)
                {
                    if (await BoardData.DeleteItemAsync(BoardSection.Todo, item.Id))
                    {
                        deleted++;
                    }
                }
            }

            await OnChanged.InvokeAsync();
            if (deleted == toDelete.Count)
            {
                await Notifier.NotifyAsync($"Deleted {deleted} done to-do(s).", Severity.Success);
            }
            else
            {
                await Notifier.NotifyAsync(
                    $"Removed {deleted} of {toDelete.Count} to-do(s). Some could not be deleted.",
                    Severity.Warning);
            }
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Something went wrong. Check your connection and try again.", Severity.Error);
        }
    }

    private async Task<bool> TryMutateAsync(Func<Task> work)
    {
        try
        {
            await work();
            await OnChanged.InvokeAsync();
            return true;
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Something went wrong. Check your connection and try again.", Severity.Error);
            return false;
        }
    }

    /// <summary>
    ///     Applies an optimistic UI change, awaits the server mutation, and rolls the optimistic change back
    ///     on failure. All board mutations should go through this so optimistic state stays consistent.
    /// </summary>
    private async Task ApplyMutationAsync(Action apply, Action rollback, Func<Task> mutation)
    {
        apply();
        _needRefresh = true;
        StateHasChanged();

        var ok = await TryMutateAsync(mutation);
        if (!ok)
        {
            rollback();
            _needRefresh = true;
            StateHasChanged();
        }
    }

    private Task ApplyOverrideAsync(Guid itemId, BoardItem optimistic, Func<Task> mutation) =>
        ApplyMutationAsync(
            () => _optimisticOverrides[itemId] = optimistic,
            () => _optimisticOverrides.Remove(itemId),
            mutation);

    private Task ApplyDeletionAsync(Guid itemId, Func<Task> mutation) =>
        ApplyMutationAsync(
            () => _optimisticDeletions.Add(itemId),
            () => _optimisticDeletions.Remove(itemId),
            mutation);

    private Task ApplyCreationAsync(Guid itemId, BoardItem optimistic, Func<Task> mutation) =>
        ApplyMutationAsync(
            () => _optimisticCreations[itemId] = optimistic,
            () => _optimisticCreations.Remove(itemId),
            mutation);

    private Task ApplySortOrderAsync(Guid itemId, double sortOrder, Func<Task> mutation) =>
        ApplyMutationAsync(
            () => _sortOrderOverrides[itemId] = sortOrder,
            () => _sortOrderOverrides.Remove(itemId),
            mutation);
}
