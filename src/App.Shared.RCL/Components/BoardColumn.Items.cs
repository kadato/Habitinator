#pragma warning disable S8949, S6966, IDE0028, IDE0042, CA1859
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class BoardColumn
{
    private readonly Dictionary<Guid, double> _sortOrderOverrides = [];
    private readonly Dictionary<Guid, BoardItem> _optimisticOverrides = [];
    private readonly HashSet<Guid> _optimisticDeletions = [];
    private readonly Dictionary<Guid, BoardItem> _optimisticCreations = [];
    private IReadOnlyList<BoardItem> _visibleCache = [];
    private List<BoardItem> _effectiveCache = [];

    protected override void OnParametersSet()
    {
        var optimisticStateCount = CountOptimisticState();
        PruneSortOrderOverrides();
        PruneOptimisticOverrides();
        PruneOptimisticDeletions();
        PruneOptimisticCreations();

        // Reinitialize the sortable only when the item list or the optimistic state actually changed.
        var itemsChanged = !ReferenceEquals(_lastItemsForRefresh, Items) || Items.Count != _lastItemsCount;
        var optimisticChanged = optimisticStateCount != CountOptimisticState();
        if (itemsChanged || optimisticChanged)
        {
            _lastItemsForRefresh = Items;
            _lastItemsCount = Items.Count;
            _needRefresh = true;
        }

        RebuildCaches();
    }

    private void RebuildCaches()
    {
        var baseItems = Items
            .Where(x => !_optimisticDeletions.Contains(x.Id))
            .Select(x => _optimisticOverrides.TryGetValue(x.Id, out var o) ? o : x);
        var serverIds = new HashSet<Guid>([.. Items.Select(x => x.Id)]);
        var creations = _optimisticCreations.Values.Where(x => !serverIds.Contains(x.Id));
        _effectiveCache = [.. baseItems.Concat(creations)];
        _visibleCache = Section switch
        {
            BoardSection.Habit => OrderBySort(FilterHabits()),
            BoardSection.Daily => OrderBySort(FilterDailies()),
            BoardSection.Todo => OrderTodos(FilterTodos()),
            _ => OrderBySort(_effectiveCache)
        };
    }

    private int CountOptimisticState() =>
        _sortOrderOverrides.Count + _optimisticOverrides.Count + _optimisticDeletions.Count + _optimisticCreations.Count;

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
                    && serverItem.DailyRepeatInterval == overrideItem.DailyRepeatInterval
                    && serverItem.DailyWeekdays == overrideItem.DailyWeekdays;
                // The server increments the streak on completion, so Counter is not compared
                // here. Otherwise a server-corrected streak would keep the override around.
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

    private IEnumerable<BoardItem> EffectiveItems => _effectiveCache;

    private int EffectiveCount => _effectiveCache.Count;

    private void InvalidateVisibleCache()
    {
        RebuildCaches();
    }

    private IReadOnlyList<BoardItem> VisibleItems()
    {
        return _visibleCache;
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
        var today = BoardToday();
        var overdue = DailyRollover.GetOverdueSince([.. EffectiveItems], today, TimeZoneService);
        return _dailyFilter switch
        {
            DailyListFilter.Due => [.. EffectiveItems.Where(d => DailySchedule.IsDueOnDate(d, today) || overdue.ContainsKey(d.Id))],
            DailyListFilter.NotDue => [.. EffectiveItems.Where(d => !DailySchedule.IsDueOnDate(d, today) && !overdue.ContainsKey(d.Id))],
            _ => [.. EffectiveItems]
        };
    }

    private DateOnly? GetDailyOverdueSince(BoardItem item)
    {
        if (Section != BoardSection.Daily)
        {
            return null;
        }

        return DailyRollover.GetOldestMiss(item, BoardToday(), TimeZoneService);
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

    private bool IsItemBusy(Guid id) => _optimisticOverrides.ContainsKey(id);

    private bool IsItemDraggable() =>
        Section != BoardSection.Todo || _todoFilter == TodoListFilter.Active;

    private async Task<bool> TryMutateAsync(Func<Task> work, string action)
    {
        try
        {
            await work();
            await OnChanged.InvokeAsync();
            return true;
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync($"Couldn't {action}. Reverted. Try again.", Severity.Error);
            return false;
        }
    }

    /// <summary>
    ///     Applies the optimistic UI change, awaits the server mutation, and rolls the change
    ///     back if the mutation fails. Send every board mutation through this method so
    ///     optimistic state stays consistent.
    /// </summary>
    private async Task ApplyMutationAsync(Action apply, Action rollback, Func<Task> mutation, string action)
    {
        apply();
        InvalidateVisibleCache();
        _needRefresh = true;
        StateHasChanged();

        var ok = await TryMutateAsync(mutation, action);
        if (!ok)
        {
            rollback();
            InvalidateVisibleCache();
            _needRefresh = true;
            StateHasChanged();
        }
    }

    private Task ApplyOverrideAsync(Guid itemId, BoardItem optimistic, Func<Task> mutation) =>
        ApplyMutationAsync(
            () => _optimisticOverrides[itemId] = optimistic,
            () => _optimisticOverrides.Remove(itemId),
            mutation,
            "save");

    private Task ApplyDeletionAsync(Guid itemId, Func<Task> mutation, string? subject = null) =>
        ApplyMutationAsync(
            () => _optimisticDeletions.Add(itemId),
            () => _optimisticDeletions.Remove(itemId),
            mutation,
            subject is null ? "delete" : $"delete \"{subject}\"");

    private Task ApplyCreationAsync(Guid itemId, BoardItem optimistic, Func<Task> mutation) =>
        ApplyMutationAsync(
            () => _optimisticCreations[itemId] = optimistic,
            () => _optimisticCreations.Remove(itemId),
            mutation,
            "add");

    private Task ApplySortOrderAsync(Guid itemId, double sortOrder, Func<Task> mutation) =>
        ApplyMutationAsync(
            () => _sortOrderOverrides[itemId] = sortOrder,
            () => _sortOrderOverrides.Remove(itemId),
            mutation,
            "reorder");
}
