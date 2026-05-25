using App.Shared.RCL.Models;

namespace App.Shared.Tests;

public sealed class TodoOrderingTests
{
    [Fact]
    public void OrderForActiveTab_puts_undated_first_then_sort_order()
    {
        var dated = new BoardItem(Guid.NewGuid(), "Dated", SortOrder: 1.0, TodoDueDate: new DateOnly(2026, 5, 1));
        var undatedLate = new BoardItem(Guid.NewGuid(), "Undated B", SortOrder: 5.0);
        var undatedEarly = new BoardItem(Guid.NewGuid(), "Undated A", SortOrder: 2.0);

        var ordered = TodoOrdering.OrderForActiveTab([dated, undatedLate, undatedEarly]);

        Assert.Equal([undatedEarly, undatedLate, dated], ordered);
    }

    [Fact]
    public void OrderForScheduledTab_sorts_by_due_date()
    {
        var late = new BoardItem(Guid.NewGuid(), "Late", TodoDueDate: new DateOnly(2026, 5, 20));
        var early = new BoardItem(Guid.NewGuid(), "Early", TodoDueDate: new DateOnly(2026, 5, 10));

        var ordered = TodoOrdering.OrderForScheduledTab([late, early]);

        Assert.Equal([early, late], ordered);
    }

    [Fact]
    public void ComputeMidpointSortOrderForActiveTab_midpoints_within_dated_block()
    {
        var datedA = new BoardItem(Guid.NewGuid(), "A", SortOrder: 10.0, TodoDueDate: new DateOnly(2026, 6, 1));
        var datedB = new BoardItem(Guid.NewGuid(), "B", SortOrder: 20.0, TodoDueDate: new DateOnly(2026, 6, 2));
        var datedC = new BoardItem(Guid.NewGuid(), "C", SortOrder: 30.0, TodoDueDate: new DateOnly(2026, 6, 3));
        var reordered = new List<BoardItem> { datedA, datedB, datedC };

        var mid = TodoOrdering.ComputeMidpointSortOrderForActiveTab(reordered, 1, x => x.SortOrder);

        Assert.Equal(20.0, mid);
    }

    [Fact]
    public void ComputeMidpointSortOrderForActiveTab_places_undated_between_undated_and_dated()
    {
        var undatedA = new BoardItem(Guid.NewGuid(), "A", SortOrder: 2.0);
        var undatedB = new BoardItem(Guid.NewGuid(), "B", SortOrder: 5.0);
        var dated = new BoardItem(Guid.NewGuid(), "Dated", SortOrder: 1.0, TodoDueDate: new DateOnly(2026, 6, 1));
        var reordered = new List<BoardItem> { undatedB, undatedA, dated };

        var mid = TodoOrdering.ComputeMidpointSortOrderForActiveTab(reordered, 1, x => x.SortOrder);

        Assert.Equal(6.0, mid);
        var moved = undatedA with { SortOrder = mid };
        Assert.True(
            TodoOrdering.GetActiveDisplayOrder(moved, x => x.SortOrder)
            < TodoOrdering.GetActiveDisplayOrder(dated, x => x.SortOrder));
    }
}
