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

    /// <summary>Incomplete first, then undated first followed by earliest due date, then the common sort keys.</summary>
    public static IOrderedEnumerable<T> SortTodos<T>(
        IEnumerable<T> items,
        Func<T, bool> isCompleted,
        Func<T, DateOnly?> dueDate,
        Func<T, double> sortOrder,
        Func<T, DateTimeOffset> createdAt,
        Func<T, Guid> id) =>
        items
            .OrderBy(x => isCompleted(x) ? 1 : 0)
            .ThenBy(x => dueDate(x) is null ? 0 : 1)
            .ThenBy(x => dueDate(x) ?? DateOnly.MaxValue)
            .ThenBy(sortOrder)
            .ThenBy(createdAt)
            .ThenBy(id);

    public static IOrderedEnumerable<T> SortTodos<T>(
        IEnumerable<T> items,
        Func<T, bool> isCompleted,
        Func<T, DateTime?> dueDateUtc,
        Func<T, double> sortOrder,
        Func<T, DateTimeOffset> createdAt,
        Func<T, Guid> id) =>
        SortTodos(
            items,
            isCompleted,
            x => dueDateUtc(x) is { } d ? DateOnly.FromDateTime(d) : null,
            sortOrder,
            createdAt,
            id);
}
