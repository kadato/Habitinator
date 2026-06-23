using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    [Inject] public GlobalTimerService TimerService { get; set; } = null!;
    [Inject] public IBoardDataService BoardData { get; set; } = null!;
    [Inject] public IDialogService DialogService { get; set; } = null!;
    [Inject] public IUserNotifier Notifier { get; set; } = null!;
    [Inject] public IUserTimeZoneService TimeZoneService { get; set; } = null!;
    [Inject] public IUserDateFormatService DateFormatService { get; set; } = null!;
    [Inject] public IUndoService UndoService { get; set; } = null!;
    [Inject] public IJSRuntime JS { get; set; } = null!;

    [Parameter][EditorRequired] public BoardSection Section { get; set; }

    [Parameter] public IReadOnlyList<BoardItem> Items { get; set; } = [];

    [Parameter] public EventCallback OnChanged { get; set; }

    [Parameter] public bool IsBoardFilterExcludingAll { get; set; }

    private string _draft = string.Empty;

    private HabitListFilter _habitFilter;
    private DailyListFilter _dailyFilter = DailyListFilter.Due;
    private TodoListFilter _todoFilter = TodoListFilter.Active;

    private void SetHabitFilter(HabitListFilter filter)
    {
        _habitFilter = filter;
        _needRefresh = true;
    }

    private void SetDailyFilter(DailyListFilter filter)
    {
        _dailyFilter = filter;
        _needRefresh = true;
    }

    private void SetTodoFilter(TodoListFilter filter)
    {
        _todoFilter = filter;
        _needRefresh = true;
    }

    private readonly Dictionary<Guid, double> _sortOrderOverrides = [];
    private readonly Dictionary<Guid, BoardItem> _optimisticOverrides = [];
    private readonly HashSet<Guid> _optimisticDeletions = [];
    private readonly Dictionary<Guid, BoardItem> _optimisticCreations = [];

    private IEnumerable<BoardItem> EffectiveItems
    {
        get
        {
            IEnumerable<BoardItem> baseItems = Items
                .Where(x => !_optimisticDeletions.Contains(x.Id))
                .Select(x => _optimisticOverrides.TryGetValue(x.Id, out BoardItem? o) ? o : x);
            HashSet<Guid> serverIds = [.. Items.Select(x => x.Id)];
            IEnumerable<BoardItem> creations = _optimisticCreations.Values.Where(x => !serverIds.Contains(x.Id));
            return baseItems.Concat(creations);
        }
    }

    private ElementReference _containerRef;
    private DotNetObjectReference<BoardColumn>? _selfRef;
    private bool _needRefresh;

    protected override void OnParametersSet()
    {
        PruneSortOrderOverrides();
        PruneOptimisticOverrides();
        PruneOptimisticDeletions();
        PruneOptimisticCreations();
        _needRefresh = true;
    }

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
        foreach (Guid id in _sortOrderOverrides.Keys.ToList())
        {
            BoardItem? item = Items.FirstOrDefault(x => x.Id == id);
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
        foreach (Guid id in _optimisticOverrides.Keys.ToList())
        {
            BoardItem? serverItem = Items.FirstOrDefault(x => x.Id == id);
            if (serverItem is null)
            {
                _optimisticOverrides.Remove(id);
                continue;
            }

            BoardItem overrideItem = _optimisticOverrides[id];
            bool match = serverItem.IsCompleted == overrideItem.IsCompleted
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
                    && serverItem.TodoDueDate == overrideItem.TodoDueDate;
            }

            if (match)
            {
                _optimisticOverrides.Remove(id);
            }
        }
    }

    private void PruneOptimisticDeletions()
    {
        foreach (Guid id in _optimisticDeletions.ToList())
        {
            BoardItem? serverItem = Items.FirstOrDefault(x => x.Id == id);
            if (serverItem is null)
            {
                _optimisticDeletions.Remove(id);
            }
        }
    }

    private void PruneOptimisticCreations()
    {
        foreach (Guid id in _optimisticCreations.Keys.Where(id => Items.Any(x => x.Id == id)).ToList())
        {
            _optimisticCreations.Remove(id);
        }
    }

    private double GetInitialSortOrderForOptimisticCreation()
    {
        IReadOnlyList<BoardItem> current = VisibleItems();
        if (current.Count == 0)
        {
            return 1.0;
        }
        double max = current.Max(x => GetEffectiveSortOrder(x) ?? 0.0);
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

    private static Variant FilterVariant(bool active)
    {
        return active ? Variant.Filled : Variant.Outlined;
    }

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
        _sortOrderOverrides.TryGetValue(item.Id, out double order) ? order : item.SortOrder;

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
        DateOnly today = DailySchedule.LocalToday(TimeZoneService);
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

    private string GetEmptyMessage()
    {
        if (IsBoardFilterExcludingAll)
        {
            return "No items match your search or tag filters.";
        }

        return "No items here yet. Add one above.";
    }

    private string GetFooterText()
    {
        return Section switch
        {
            BoardSection.Habit => "These are your habits — use +/− to log a rep and undo. Click the title for notes and subtasks. The global timer can follow the habit you trigger.",
            BoardSection.Daily => "Dailies are for recurring work — use the square when you are done for today. Click the title to edit details, or the trash icon to delete.",
            BoardSection.Todo => "To Do's are one-off. On Active, drag cards to reorder; Scheduled sorts by due date. Click the title to edit details.",
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

    // ── Reordering (SortableJS) ─────────────────────────────

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

        BoardItem sourceItem = visible[oldIndex];
        List<BoardItem> reordered = [.. visible];
        reordered.RemoveAt(oldIndex);
        reordered.Insert(newIndex, sourceItem);

        double? newSortOrder = Section == BoardSection.Todo && _todoFilter == TodoListFilter.Active
            ? TodoOrdering.ComputeMidpointSortOrderForActiveTab(reordered, newIndex, GetEffectiveSortOrder)
            : BoardItemReorder.ComputeMidpointSortOrder(reordered, newIndex, GetEffectiveSortOrder);

        if (newSortOrder is not { } sortOrder)
        {
            _needRefresh = true;
            StateHasChanged();
            return;
        }

        _sortOrderOverrides[sourceItem.Id] = sortOrder;
        _needRefresh = true;
        StateHasChanged();

        bool ok = await TryMutateAsync(() => PersistReorderAsync(sourceItem, sortOrder));
        if (!ok)
        {
            _sortOrderOverrides.Remove(sourceItem.Id);
            _needRefresh = true;
            StateHasChanged();
            return;
        }
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
                item.Title,
                item.Notes,
                item.Tags,
                item.TrackPlus,
                item.TrackMinus,
                item.ResetPeriod,
                item.Counter,
                item.NegativeCounter,
                item.ChecklistJson,
                sortOrder: newSortOrder),

            BoardSection.Daily => BoardData.UpdateDailyAsync(
                item.Id,
                item.Title,
                item.Notes,
                item.Tags,
                item.DailyStartDate?.ToDateTime(TimeOnly.MinValue),
                item.DailyRepeat,
                item.DailyRepeatInterval,
                item.ChecklistJson,
                item.Counter,
                sortOrder: newSortOrder),

            BoardSection.Todo => BoardData.UpdateTodoAsync(
                item.Id,
                item.Title,
                item.Notes,
                item.Tags,
                item.ChecklistJson,
                item.TodoDueDate?.ToDateTime(TimeOnly.MinValue),
                sortOrder: newSortOrder),

            _ => Task.CompletedTask
        };
    }

    private DateOnly BoardToday() => DailySchedule.LocalToday(TimeZoneService);

    private async Task SetChecklistItemDoneAsync(BoardItem item, Guid lineId, bool done)
    {
        IReadOnlyList<DailyChecklistItem> rows = DailyChecklistJson.Parse(item.ChecklistJson);
        List<DailyChecklistItem> list = [.. rows];
        int i = list.FindIndex(x => x.Id == lineId);
        if (i < 0 || list[i].IsDone == done)
        {
            return;
        }

        list[i] = list[i] with { IsDone = done };
        string? json = DailyChecklistJson.Serialize(list);
        if (json is null)
        {
            return;
        }

        BoardItem optimistic = item with { ChecklistJson = json };
        _optimisticOverrides[item.Id] = optimistic;
        _needRefresh = true;
        StateHasChanged();

        Task mutation;
        if (Section == BoardSection.Daily)
        {
            mutation = BoardData.UpdateDailyAsync(
                item.Id,
                item.Title,
                item.Notes,
                item.Tags,
                item.DailyStartDate?.ToDateTime(TimeOnly.MinValue),
                item.DailyRepeat,
                item.DailyRepeatInterval,
                json,
                item.Counter);
        }
        else if (Section == BoardSection.Todo)
        {
            mutation = BoardData.UpdateTodoAsync(
                item.Id,
                item.Title,
                item.Notes,
                item.Tags,
                json,
                item.TodoDueDate?.ToDateTime(TimeOnly.MinValue));
        }
        else
        {
            mutation = BoardData.UpdateHabitAsync(
                item.Id,
                item.Title,
                item.Notes,
                item.Tags,
                item.TrackPlus,
                item.TrackMinus,
                item.ResetPeriod,
                item.Counter,
                item.NegativeCounter,
                json);
        }

        bool ok = await TryMutateAsync(() => mutation);
        if (!ok)
        {
            _optimisticOverrides.Remove(item.Id);
            _needRefresh = true;
            StateHasChanged();
        }
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

        string title = _draft.Trim();
        Guid newId = Guid.NewGuid();
        BoardItem tempItem = new(
            Id: newId,
            Title: title,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            SortOrder: GetInitialSortOrderForOptimisticCreation()
        );
        _optimisticCreations[newId] = tempItem;
        _draft = string.Empty;
        _needRefresh = true;
        StateHasChanged();

        bool ok = await TryMutateAsync(() => BoardData.CreateItemAsync(Section, title, newId));
        if (!ok)
        {
            _optimisticCreations.Remove(newId);
            _needRefresh = true;
            StateHasChanged();
        }
    }

    private async Task HabitUpAsync(BoardItem item)
    {
        BoardItem optimistic = item with { Counter = item.Counter + 1 };
        _optimisticOverrides[item.Id] = optimistic;
        _needRefresh = true;
        StateHasChanged();

        bool ok = await TryMutateAsync(() => BoardData.IncrementHabitPlusAsync(item.Id));
        if (!ok)
        {
            _optimisticOverrides.Remove(item.Id);
            _needRefresh = true;
            StateHasChanged();
        }
    }

    private async Task HabitDownAsync(BoardItem item)
    {
        BoardItem optimistic = item with { NegativeCounter = item.NegativeCounter + 1 };
        _optimisticOverrides[item.Id] = optimistic;
        _needRefresh = true;
        StateHasChanged();

        bool ok = await TryMutateAsync(() => BoardData.IncrementHabitMinusAsync(item.Id));
        if (!ok)
        {
            _optimisticOverrides.Remove(item.Id);
            _needRefresh = true;
            StateHasChanged();
        }
    }

    private async Task ToggleAsync(BoardItem item)
    {
        bool nextCompleted = !item.IsCompleted;
        DateOnly? lastCompleted = nextCompleted ? BoardToday() : null;
        BoardItem optimistic = item with { IsCompleted = nextCompleted, DailyLastCompletedOn = lastCompleted };
        _optimisticOverrides[item.Id] = optimistic;
        _needRefresh = true;
        StateHasChanged();

        bool ok = await TryMutateAsync(() => BoardData.ToggleItemAsync(Section, item.Id));
        if (!ok)
        {
            _optimisticOverrides.Remove(item.Id);
            _needRefresh = true;
            StateHasChanged();
        }
    }

    private async Task OpenEditHabitAsync(BoardItem item)
    {
        DialogOptions options = new()
        {
            MaxWidth = MaxWidth.Small,
            FullWidth = false,
            CloseButton = false,
            CloseOnEscapeKey = true,
            NoHeader = true
        };
        DialogParameters<EditHabitDialog> parameters = new() { { x => x.Item, item } };
        IDialogReference dialog = await DialogService.ShowAsync<EditHabitDialog>(string.Empty, parameters, options);
        DialogResult? result = await dialog.Result;
        if (result is { Canceled: false, Data: EditHabitDialogResult r })
        {
            await HandleEditHabitResultAsync(item, r);
        }
    }

    private async Task HandleEditHabitResultAsync(BoardItem item, EditHabitDialogResult r)
    {
        switch (r.Action)
        {
            case EditDialogAction.Save:
                await SaveHabitAsync(item, r);
                break;
            case EditDialogAction.Archive:
                await ArchiveHabitAsync(item);
                break;
            case EditDialogAction.Delete:
                await DeleteHabitAsync(item);
                break;
        }
    }

    private Task SaveHabitAsync(BoardItem item, EditHabitDialogResult r)
    {
        BoardItem optimistic = item with
        {
            Title = r.Title,
            Notes = r.Notes,
            Tags = r.Tags,
            TrackPlus = r.TrackPlus,
            TrackMinus = r.TrackMinus,
            ResetPeriod = r.ResetPeriod,
            Counter = r.Counter,
            NegativeCounter = r.NegativeCounter,
            ChecklistJson = r.ChecklistJson
        };
        _optimisticOverrides[item.Id] = optimistic;
        _needRefresh = true;
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            bool ok = await TryMutateAsync(() => BoardData.UpdateHabitAsync(
                item.Id,
                r.Title,
                r.Notes,
                r.Tags,
                r.TrackPlus,
                r.TrackMinus,
                r.ResetPeriod,
                r.Counter,
                r.NegativeCounter,
                r.ChecklistJson
            ));
            if (!ok)
            {
                _optimisticOverrides.Remove(item.Id);
                _needRefresh = true;
                await InvokeAsync(StateHasChanged);
            }
        });
        return Task.CompletedTask;
    }

    private Task ArchiveHabitAsync(BoardItem item)
    {
        _optimisticDeletions.Add(item.Id);
        _needRefresh = true;
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            bool ok = await TryMutateAsync(() => BoardData.ArchiveItemAsync(BoardSection.Habit, item.Id));
            if (!ok)
            {
                _optimisticDeletions.Remove(item.Id);
                _needRefresh = true;
                await InvokeAsync(StateHasChanged);
            }
        });
        return Task.CompletedTask;
    }

    private Task DeleteHabitAsync(BoardItem item)
    {
        _optimisticDeletions.Add(item.Id);
        _needRefresh = true;
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            bool ok = await TryMutateAsync(() => BoardData.DeleteItemAsync(BoardSection.Habit, item.Id));
            if (!ok)
            {
                _optimisticDeletions.Remove(item.Id);
                _needRefresh = true;
                await InvokeAsync(StateHasChanged);
            }
        });
        return Task.CompletedTask;
    }

    private async Task OpenEditDailyAsync(BoardItem item)
    {
        DialogOptions options = new()
        {
            MaxWidth = MaxWidth.Small,
            FullWidth = false,
            CloseButton = false,
            CloseOnEscapeKey = true,
            NoHeader = true
        };
        DialogParameters<EditDailyDialog> parameters = new() { { x => x.Item, item } };
        IDialogReference dialog = await DialogService.ShowAsync<EditDailyDialog>(string.Empty, parameters, options);
        DialogResult? result = await dialog.Result;
        if (result is { Canceled: false, Data: EditDailyDialogResult r })
        {
            await HandleEditDailyResultAsync(item, r);
        }
    }

    private async Task HandleEditDailyResultAsync(BoardItem item, EditDailyDialogResult r)
    {
        switch (r.Action)
        {
            case EditDialogAction.Save:
                await SaveDailyAsync(item, r);
                break;
            case EditDialogAction.Archive:
                await ArchiveDailyAsync(item);
                break;
            case EditDialogAction.Delete:
                await DeleteDailyAsync(item);
                break;
        }
    }

    private Task SaveDailyAsync(BoardItem item, EditDailyDialogResult r)
    {
        BoardItem optimistic = item with
        {
            Title = r.Title,
            Notes = r.Notes,
            Tags = r.Tags,
            DailyStartDate = DateOnly.FromDateTime(r.StartDate),
            DailyRepeat = r.Repeat,
            DailyRepeatInterval = r.RepeatInterval,
            ChecklistJson = r.ChecklistJson,
            Counter = r.Streak
        };
        _optimisticOverrides[item.Id] = optimistic;
        _needRefresh = true;
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            bool ok = await TryMutateAsync(() => BoardData.UpdateDailyAsync(
                item.Id,
                r.Title,
                r.Notes,
                r.Tags,
                r.StartDate,
                r.Repeat,
                r.RepeatInterval,
                r.ChecklistJson,
                r.Streak
            ));
            if (!ok)
            {
                _optimisticOverrides.Remove(item.Id);
                _needRefresh = true;
                await InvokeAsync(StateHasChanged);
            }
        });
        return Task.CompletedTask;
    }

    private Task ArchiveDailyAsync(BoardItem item)
    {
        _optimisticDeletions.Add(item.Id);
        _needRefresh = true;
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            bool ok = await TryMutateAsync(() => BoardData.ArchiveItemAsync(BoardSection.Daily, item.Id));
            if (!ok)
            {
                _optimisticDeletions.Remove(item.Id);
                _needRefresh = true;
                await InvokeAsync(StateHasChanged);
            }
        });
        return Task.CompletedTask;
    }

    private Task DeleteDailyAsync(BoardItem item)
    {
        _optimisticDeletions.Add(item.Id);
        _needRefresh = true;
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            bool ok = await TryMutateAsync(() => BoardData.DeleteItemAsync(BoardSection.Daily, item.Id));
            if (!ok)
            {
                _optimisticDeletions.Remove(item.Id);
                _needRefresh = true;
                await InvokeAsync(StateHasChanged);
            }
        });
        return Task.CompletedTask;
    }

    private async Task OpenEditTodoAsync(BoardItem item)
    {
        DialogOptions options = new()
        {
            MaxWidth = MaxWidth.Small,
            FullWidth = false,
            CloseButton = false,
            CloseOnEscapeKey = true,
            NoHeader = true
        };
        DialogParameters<EditTodoDialog> parameters = new() { { x => x.Item, item } };
        IDialogReference dialog = await DialogService.ShowAsync<EditTodoDialog>(string.Empty, parameters, options);
        DialogResult? result = await dialog.Result;
        if (result is { Canceled: false, Data: EditTodoDialogResult r })
        {
            await HandleEditTodoResultAsync(item, r);
        }
    }

    private async Task HandleEditTodoResultAsync(BoardItem item, EditTodoDialogResult r)
    {
        switch (r.Action)
        {
            case EditDialogAction.Save:
                await SaveTodoAsync(item, r);
                break;
            case EditDialogAction.Archive:
                await ArchiveTodoAsync(item);
                break;
            case EditDialogAction.Delete:
                await DeleteTodoAsync(item);
                break;
        }
    }

    private Task SaveTodoAsync(BoardItem item, EditTodoDialogResult r)
    {
        BoardItem optimistic = item with
        {
            Title = r.Title,
            Notes = r.Notes,
            Tags = r.Tags,
            ChecklistJson = r.ChecklistJson,
            TodoDueDate = r.DueDate != null ? DateOnly.FromDateTime(r.DueDate.Value) : null
        };
        _optimisticOverrides[item.Id] = optimistic;
        _needRefresh = true;
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            bool ok = await TryMutateAsync(() => BoardData.UpdateTodoAsync(
                item.Id,
                r.Title,
                r.Notes,
                r.Tags,
                r.ChecklistJson,
                r.DueDate
            ));
            if (!ok)
            {
                _optimisticOverrides.Remove(item.Id);
                _needRefresh = true;
                await InvokeAsync(StateHasChanged);
            }
        });
        return Task.CompletedTask;
    }

    private Task ArchiveTodoAsync(BoardItem item)
    {
        _optimisticDeletions.Add(item.Id);
        _needRefresh = true;
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            bool ok = await TryMutateAsync(() => BoardData.ArchiveItemAsync(BoardSection.Todo, item.Id));
            if (!ok)
            {
                _optimisticDeletions.Remove(item.Id);
                _needRefresh = true;
                await InvokeAsync(StateHasChanged);
            }
        });
        return Task.CompletedTask;
    }

    private Task DeleteTodoAsync(BoardItem item)
    {
        _optimisticDeletions.Add(item.Id);
        _needRefresh = true;
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            bool ok = await TryMutateAsync(() => BoardData.DeleteItemAsync(BoardSection.Todo, item.Id));
            if (!ok)
            {
                _optimisticDeletions.Remove(item.Id);
                _needRefresh = true;
                await InvokeAsync(StateHasChanged);
            }
        });
        return Task.CompletedTask;
    }

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

    private async Task DeleteAsync(Guid id)
    {
        _optimisticDeletions.Add(id);
        _needRefresh = true;
        StateHasChanged();

        bool ok = await TryMutateAsync(() => BoardData.DeleteItemAsync(Section, id));
        if (!ok)
        {
            _optimisticDeletions.Remove(id);
            _needRefresh = true;
            StateHasChanged();
        }
    }

    private async Task MoveToTopAsync(BoardItem item)
    {
        List<BoardItem> visible = [.. VisibleItems()];
        int sourceIndex = visible.FindIndex(x => x.Id == item.Id);
        if (sourceIndex <= 0)
        {
            return;
        }

        List<BoardItem> reordered = [.. visible];
        reordered.RemoveAt(sourceIndex);
        reordered.Insert(0, item);

        double? newSortOrder = Section == BoardSection.Todo && _todoFilter == TodoListFilter.Active
            ? TodoOrdering.ComputeMidpointSortOrderForActiveTab(reordered, 0, GetEffectiveSortOrder)
            : BoardItemReorder.ComputeMidpointSortOrder(reordered, 0, GetEffectiveSortOrder);

        if (newSortOrder is not { } sortOrder)
        {
            return;
        }

        _sortOrderOverrides[item.Id] = sortOrder;
        _needRefresh = true;
        StateHasChanged();

        bool ok = await TryMutateAsync(() => PersistReorderAsync(item, sortOrder));
        if (!ok)
        {
            _sortOrderOverrides.Remove(item.Id);
            _needRefresh = true;
            StateHasChanged();
        }
    }

    private async Task MoveToBottomAsync(BoardItem item)
    {
        List<BoardItem> visible = [.. VisibleItems()];
        int sourceIndex = visible.FindIndex(x => x.Id == item.Id);
        if (sourceIndex < 0 || sourceIndex == visible.Count - 1)
        {
            return;
        }

        List<BoardItem> reordered = [.. visible];
        reordered.RemoveAt(sourceIndex);
        reordered.Add(item);
        int insertAt = reordered.Count - 1;

        double? newSortOrder = Section == BoardSection.Todo && _todoFilter == TodoListFilter.Active
            ? TodoOrdering.ComputeMidpointSortOrderForActiveTab(reordered, insertAt, GetEffectiveSortOrder)
            : BoardItemReorder.ComputeMidpointSortOrder(reordered, insertAt, GetEffectiveSortOrder);

        if (newSortOrder is not { } sortOrder)
        {
            return;
        }

        _sortOrderOverrides[item.Id] = sortOrder;
        _needRefresh = true;
        StateHasChanged();

        bool ok = await TryMutateAsync(() => PersistReorderAsync(item, sortOrder));
        if (!ok)
        {
            _sortOrderOverrides.Remove(item.Id);
            _needRefresh = true;
            StateHasChanged();
        }
    }

    private async Task DeleteAllDoneTodosAsync()
    {
        if (Section != BoardSection.Todo || _todoFilter != TodoListFilter.Done)
        {
            return;
        }

        List<BoardItem> toDelete = FilterTodos();
        if (toDelete.Count == 0)
        {
            return;
        }

        bool? confirmed = await DialogService.ShowMessageBoxAsync(
            "Delete all done to-dos?",
            $"This will permanently remove {toDelete.Count} done to-do(s) matching your current view. This cannot be undone.",
            "Delete all",
            null,
            "Cancel");

        if (confirmed != true)
        {
            return;
        }

        int deleted = 0;
        try
        {
            using (UndoService.BeginBatch($"Delete {toDelete.Count} done to-dos"))
            {
                foreach (BoardItem item in toDelete)
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

    private MudTextField<string>? _draftField;

    public async Task FocusDraftAsync()
    {
        if (_draftField is not null)
        {
            await _draftField.FocusAsync();
        }
    }
}
