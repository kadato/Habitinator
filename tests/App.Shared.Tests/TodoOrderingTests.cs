using App.Shared.RCL.Models;

namespace App.Shared.Tests;

public sealed class TodoOrderingTests
{
    [Fact]
    public void OrderForActiveTab_sorts_by_sort_order_only()
    {
        var dated = new BoardItem(Guid.NewGuid(), "Dated", SortOrder: 1.0, TodoDueDate: new DateOnly(2026, 5, 1));
        var undatedLate = new BoardItem(Guid.NewGuid(), "Undated B", SortOrder: 5.0);
        var undatedEarly = new BoardItem(Guid.NewGuid(), "Undated A", SortOrder: 2.0);

        var ordered = TodoOrdering.OrderForActiveTab([dated, undatedLate, undatedEarly]);

        Assert.Equal([dated, undatedEarly, undatedLate], ordered);
    }

    [Fact]
    public void OrderForScheduledTab_sorts_by_due_date()
    {
        var late = new BoardItem(Guid.NewGuid(), "Late", TodoDueDate: new DateOnly(2026, 5, 20));
        var early = new BoardItem(Guid.NewGuid(), "Early", TodoDueDate: new DateOnly(2026, 5, 10));

        var ordered = TodoOrdering.OrderForScheduledTab([late, early]);

        Assert.Equal([early, late], ordered);
    }
}
