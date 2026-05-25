namespace App.Shared.RCL.Models;

/// <summary>
/// Pure helpers for computing list insert positions and midpoint <see cref="BoardItem.SortOrder"/> values.
/// </summary>
public static class BoardItemReorder
{
    /// <summary>Sort order for a newly created item so it appears at the top of its column.</summary>
    public static double SortOrderForNewItem(double? currentMinimum) => (currentMinimum ?? 1.0) - 1.0;

    /// <summary>
    /// Inserts <paramref name="sourceIndex"/> relative to <paramref name="targetIndex"/> after removing the source.
    /// When <paramref name="insertBefore"/> is false, inserts after the target (drag-down semantics).
    /// </summary>
    public static int ComputeInsertIndex(int sourceIndex, int targetIndex, bool insertBefore)
    {
        if (sourceIndex == targetIndex)
        {
            return sourceIndex;
        }

        var targetIndexAfterRemove = sourceIndex < targetIndex ? targetIndex - 1 : targetIndex;
        return insertBefore ? targetIndexAfterRemove : targetIndexAfterRemove + 1;
    }

    /// <summary>Insert index when moving one slot up in the list.</summary>
    public static int? ComputeMoveUpIndex(int sourceIndex) =>
        sourceIndex > 0 ? sourceIndex - 1 : null;

    /// <summary>Insert index when moving one slot down in the list.</summary>
    public static int? ComputeMoveDownIndex(int sourceIndex, int listCount) =>
        sourceIndex < listCount - 1 ? sourceIndex + 1 : null;

    /// <summary>
    /// Midpoint sort order for an item inserted at <paramref name="insertAt"/> in <paramref name="reordered"/>.
    /// Neighbour values come from each item's effective sort order.
    /// </summary>
    public static double? ComputeMidpointSortOrder(
        IReadOnlyList<BoardItem> reordered,
        int insertAt,
        Func<BoardItem, double?> getSortOrder)
    {
        double? prevOrder = insertAt > 0
            ? ResolveSortOrder(getSortOrder(reordered[insertAt - 1]), insertAt - 1)
            : null;
        double? nextOrder = insertAt < reordered.Count - 1
            ? ResolveSortOrder(getSortOrder(reordered[insertAt + 1]), insertAt + 1)
            : null;

        if (prevOrder is null && nextOrder is not null)
        {
            return nextOrder.Value - 1.0;
        }

        if (nextOrder is null && prevOrder is not null)
        {
            return prevOrder.Value + 1.0;
        }

        if (prevOrder is not null && nextOrder is not null)
        {
            return (prevOrder.Value + nextOrder.Value) / 2.0;
        }

        return insertAt;
    }

    /// <summary>
    /// Uses list position when <see cref="BoardItem.SortOrder"/> is unset so reorder still works for legacy rows.
    /// </summary>
    private static double ResolveSortOrder(double? sortOrder, int listIndex) =>
        sortOrder ?? listIndex;

    /// <summary>
    /// Builds a new list order and midpoint sort order for moving <paramref name="sourceId"/>
    /// relative to <paramref name="targetId"/>.
    /// </summary>
    public static (List<BoardItem> Reordered, double? NewSortOrder)? TryComputeReorder(
        IReadOnlyList<BoardItem> visible,
        Guid sourceId,
        Guid targetId,
        bool insertBefore,
        Func<BoardItem, double?> getSortOrder)
    {
        if (sourceId == targetId)
        {
            return null;
        }

        var sourceItem = visible.FirstOrDefault(x => x.Id == sourceId);
        var targetItem = visible.FirstOrDefault(x => x.Id == targetId);
        if (sourceItem is null || targetItem is null)
        {
            return null;
        }

        var reordered = visible.ToList();
        var sourceIndex = reordered.IndexOf(sourceItem);
        var targetIndex = reordered.IndexOf(targetItem);
        reordered.RemoveAt(sourceIndex);

        var insertAt = ComputeInsertIndex(sourceIndex, targetIndex, insertBefore);
        reordered.Insert(insertAt, sourceItem);

        var newSortOrder = ComputeMidpointSortOrder(reordered, insertAt, getSortOrder);
        return newSortOrder is null ? null : (reordered, newSortOrder);
    }
}
