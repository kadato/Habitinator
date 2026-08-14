namespace App.Shared.RCL.Models;

/// <summary>
///     Single source of truth for the board snapshot ordering rules shared by the server
///     <c>BoardPersistenceService.BuildSnapshot</c> and the MAUI local mirror
///     <c>LocalFirstBoardDataService.OrderRows</c>. Both clients must render the same order.
/// </summary>
public static class BoardOrdering
{
    public static IOrderedEnumerable<T> SortHabits<T>(
        IEnumerable<T> items,
        Func<T, double> sortOrder,
        Func<T, DateTimeOffset> createdAt,
        Func<T, Guid> id) =>
        items.OrderBy(sortOrder).ThenBy(createdAt).ThenBy(id);

    public static IOrderedEnumerable<T> SortDailies<T>(
        IEnumerable<T> items,
        Func<T, bool> completeForToday,
        Func<T, double> sortOrder,
        Func<T, DateTimeOffset> createdAt,
        Func<T, Guid> id) =>
        items.OrderBy(x => completeForToday(x) ? 1 : 0).ThenBy(sortOrder).ThenBy(createdAt).ThenBy(id);

    /// <summary>Incomplete first, then by due date in UTC with undated last, then the common sort keys.</summary>
    public static IOrderedEnumerable<T> SortTodos<T>(
        IEnumerable<T> items,
        Func<T, bool> isCompleted,
        Func<T, DateTime?> dueDateUtc,
        Func<T, double> sortOrder,
        Func<T, DateTimeOffset> createdAt,
        Func<T, Guid> id) =>
        items
            .OrderBy(x => isCompleted(x) ? 1 : 0)
            .ThenBy(x => dueDateUtc(x) is null ? 0 : 1)
            .ThenBy(x => dueDateUtc(x) ?? DateTime.MaxValue)
            .ThenBy(sortOrder)
            .ThenBy(createdAt)
            .ThenBy(id);
}
