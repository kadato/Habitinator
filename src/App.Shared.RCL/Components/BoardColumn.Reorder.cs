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
    private readonly string _columnInstanceId = Guid.NewGuid().ToString("N");
    private ElementReference _containerRef;
    private DotNetObjectReference<BoardColumn>? _selfRef;
    private bool _needRefresh;
    private IReadOnlyList<BoardItem>? _lastItemsForRefresh;
    private int _lastItemsCount = -1;

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
            await JS.InvokeVoidAsync("HabitinatorSortable.init", _columnInstanceId, _containerRef, _selfRef);
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

    private Task PersistReorderAsync(BoardItem item, double newSortOrder)
    {
        return Section switch
        {
            BoardSection.Habit => BoardData.UpdateHabitAsync(
                item.Id,
                UpdateHabitArgs.From(item) with { SortOrder = newSortOrder }),

            BoardSection.Daily => BoardData.UpdateDailyAsync(
                item.Id,
                UpdateDailyArgs.From(item) with { SortOrder = newSortOrder }),

            BoardSection.Todo => BoardData.UpdateTodoAsync(
                item.Id,
                UpdateTodoArgs.From(item) with { SortOrder = newSortOrder }),

            _ => Task.CompletedTask
        };
    }

    private Task MoveToTopAsync(BoardItem item) => MoveToIndexAsync(item, 0);

    private Task MoveToBottomAsync(BoardItem item) => MoveToIndexAsync(item, VisibleItems().Count - 1);

    private Task MoveUpAsync(BoardItem item)
    {
        List<BoardItem> visible = [.. VisibleItems()];
        var sourceIndex = visible.FindIndex(x => x.Id == item.Id);
        if (sourceIndex <= 0)
        {
            return Task.CompletedTask;
        }

        return MoveToIndexAsync(item, sourceIndex - 1);
    }

    private Task MoveDownAsync(BoardItem item)
    {
        List<BoardItem> visible = [.. VisibleItems()];
        var sourceIndex = visible.FindIndex(x => x.Id == item.Id);
        if (sourceIndex < 0 || sourceIndex >= visible.Count - 1)
        {
            return Task.CompletedTask;
        }

        return MoveToIndexAsync(item, sourceIndex + 1);
    }

    private async Task MoveToIndexAsync(BoardItem item, int targetIndex)
    {
        List<BoardItem> visible = [.. VisibleItems()];
        var sourceIndex = visible.FindIndex(x => x.Id == item.Id);
        if (sourceIndex < 0 || sourceIndex == targetIndex || targetIndex < 0 || targetIndex >= visible.Count)
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
}
