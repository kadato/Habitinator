namespace App.Shared.RCL.Models;

/// <summary>
/// Sort helpers for to-do list tabs.
/// </summary>
public static class TodoOrdering
{
    /// <summary>
    /// Display-order offset so dated to-dos sort after all undated items while sharing one drag/reorder space.
    /// </summary>
    public const double DatedSortOrderOffset = 1_000_000_000.0;

    /// <summary>Active tab: undated first, then manual order. Uses a unified display order for drag midpoints.</summary>
    public static IReadOnlyList<BoardItem> OrderForActiveTab(
        IEnumerable<BoardItem> items,
        Func<BoardItem, double?>? getSortOrder = null)
    {
        getSortOrder ??= static x => x.SortOrder;
        return [.. items
            .OrderBy(x => GetActiveDisplayOrder(x, getSortOrder))
            .ThenBy(x => x.Id)];
    }

    /// <summary>Scheduled tab: due date ascending (caller filters to dated items only).</summary>
    public static IReadOnlyList<BoardItem> OrderForScheduledTab(IEnumerable<BoardItem> items)
    {
        return [.. items
            .OrderBy(x => x.TodoDueDate ?? DateOnly.MaxValue)
            .ThenBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.Id)];
    }

    /// <summary>Maps stored <see cref="BoardItem.SortOrder"/> to the value used for Active-tab ordering and drag midpoints.</summary>
    public static double GetActiveDisplayOrder(BoardItem item, Func<BoardItem, double?> getSortOrder)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(getSortOrder);
        var order = getSortOrder(item) ?? double.MaxValue;
        return item.TodoDueDate.HasValue ? DatedSortOrderOffset + order : order;
    }

    /// <summary>Converts a display-order midpoint back to persisted <see cref="BoardItem.SortOrder"/>.</summary>
    public static double ToStoredSortOrder(BoardItem item, double displayOrder)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.TodoDueDate.HasValue ? displayOrder - DatedSortOrderOffset : displayOrder;
    }

    /// <summary>
    /// Midpoint sort order after a drop on the Active tab. Neighbours use the same ordering as <see cref="OrderForActiveTab"/>.
    /// </summary>
    public static double? ComputeMidpointSortOrderForActiveTab(
        IReadOnlyList<BoardItem> reordered,
        int insertAt,
        Func<BoardItem, double?> getSortOrder)
    {
        ArgumentNullException.ThrowIfNull(reordered);
        ArgumentNullException.ThrowIfNull(getSortOrder);
        var item = reordered[insertAt];
        var hasPrev = insertAt > 0;
        var hasNext = insertAt < reordered.Count - 1;
        var prev = hasPrev ? reordered[insertAt - 1] : null;
        var next = hasNext ? reordered[insertAt + 1] : null;
        var itemDated = item.TodoDueDate.HasValue;
        var prevDated = prev?.TodoDueDate.HasValue ?? false;
        var nextDated = next?.TodoDueDate.HasValue ?? false;

        // Undated item placed just above the dated block: step above previous undated neighbour.
        if (prev is { } prevItem && IsPlacedJustAboveDatedBlock(itemDated, true, prevDated, hasNext, nextDated))
        {
            return ToStoredSortOrder(item, GetActiveDisplayOrder(prevItem, getSortOrder) + 1.0);
        }

        // Dated item placed at the top of the dated block (directly under undated items).
        if (IsPlacedAtTopOfDatedBlock(itemDated, hasPrev, prevDated))
        {
            if (next is { } nextItem && nextDated)
            {
                var nextDisplay = GetActiveDisplayOrder(nextItem, getSortOrder);
                var midDisplay = (DatedSortOrderOffset + nextDisplay) / 2.0;
                return ToStoredSortOrder(item, midDisplay);
            }

            return ToStoredSortOrder(item, DatedSortOrderOffset + 1.0);
        }

        // Undated at the very top (no undated neighbour above).
        if (IsPlacedAtVeryTop(itemDated, hasPrev))
        {
            if (next is { } nextItemTop && !nextDated)
            {
                return ToStoredSortOrder(item, GetActiveDisplayOrder(nextItemTop, getSortOrder) - 1.0);
            }

            return BoardItemReorder.SortOrderForNewItem(
                reordered.Where(x => !x.TodoDueDate.HasValue).Min(getSortOrder));
        }

        var displayMid = BoardItemReorder.ComputeMidpointSortOrder(
            reordered,
            insertAt,
            i => GetActiveDisplayOrder(i, getSortOrder));
        return displayMid is null ? null : ToStoredSortOrder(item, displayMid.Value);
    }

    private static bool IsPlacedJustAboveDatedBlock(bool itemDated, bool hasPrev, bool prevDated, bool hasNext, bool nextDated) =>
        !itemDated && hasPrev && !prevDated && (!hasNext || nextDated);

    private static bool IsPlacedAtTopOfDatedBlock(bool itemDated, bool hasPrev, bool prevDated) =>
        itemDated && hasPrev && !prevDated;

    private static bool IsPlacedAtVeryTop(bool itemDated, bool hasPrev) =>
        !itemDated && !hasPrev;
}
