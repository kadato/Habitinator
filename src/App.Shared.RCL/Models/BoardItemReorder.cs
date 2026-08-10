namespace App.Shared.RCL.Models;

/// <summary>
/// Pure helpers for computing list insert positions and midpoint <see cref="BoardItem.SortOrder"/> values.
/// </summary>
public static class BoardItemReorder
{
    /// <summary>Sort order for a newly created item so it appears at the top of its column.</summary>
    public static double SortOrderForNewItem(double? currentMinimum) => (currentMinimum ?? 1.0) - 1.0;

    /// <summary>
    /// Midpoint sort order for an item inserted at <paramref name="insertAt"/> in <paramref name="reordered"/>.
    /// Neighbour values come from each item's effective sort order.
    /// </summary>
    public static double? ComputeMidpointSortOrder(
        IReadOnlyList<BoardItem> reordered,
        int insertAt,
        Func<BoardItem, double?> getSortOrder)
    {
        ArgumentNullException.ThrowIfNull(reordered);
        ArgumentNullException.ThrowIfNull(getSortOrder);
        double? prevOrder = insertAt > 0
            ? ResolveSortOrder(getSortOrder(reordered[insertAt - 1]), insertAt - 1)
            : null;
        double? nextOrder = insertAt < reordered.Count - 1
            ? ResolveSortOrder(getSortOrder(reordered[insertAt + 1]), insertAt + 1)
            : null;

        if (prevOrder is not null)
        {
            if (nextOrder is not null)
            {
                return (prevOrder.Value + nextOrder.Value) / 2.0;
            }

            return prevOrder.Value + 1.0;
        }

        return nextOrder is not null ? nextOrder.Value - 1.0 : insertAt;
    }

    /// <summary>
    /// Uses list position when <see cref="BoardItem.SortOrder"/> is unset so reorder still works for legacy rows.
    /// </summary>
    private static double ResolveSortOrder(double? sortOrder, int listIndex) =>
        sortOrder ?? listIndex;
}
