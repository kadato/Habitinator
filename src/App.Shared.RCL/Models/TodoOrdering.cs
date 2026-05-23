namespace App.Shared.RCL.Models;

/// <summary>
/// Sort helpers for to-do list tabs.
/// </summary>
public static class TodoOrdering
{
    /// <summary>Active tab: manual order via <see cref="BoardItem.SortOrder"/> only.</summary>
    public static IReadOnlyList<BoardItem> OrderForActiveTab(IEnumerable<BoardItem> items) =>
        items
            .OrderBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.Id)
            .ToList();

    /// <summary>Scheduled tab: due date ascending (caller filters to dated items only).</summary>
    public static IReadOnlyList<BoardItem> OrderForScheduledTab(IEnumerable<BoardItem> items) =>
        items
            .OrderBy(x => x.TodoDueDate ?? DateOnly.MaxValue)
            .ThenBy(x => x.SortOrder ?? double.MaxValue)
            .ThenBy(x => x.Id)
            .ToList();
}
