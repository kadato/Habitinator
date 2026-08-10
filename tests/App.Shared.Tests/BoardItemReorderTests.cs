using App.Shared.RCL.Models;

namespace App.Shared.Tests;

public sealed class BoardItemReorderTests
{
    [Fact]
    public void SortOrderForNewItem_is_below_current_minimum()
    {
        Assert.Equal(4.0, BoardItemReorder.SortOrderForNewItem(5.0));
        Assert.Equal(0.0, BoardItemReorder.SortOrderForNewItem(null));
    }

    [Fact]
    public void ComputeMidpointSortOrder_between_neighbours()
    {
        var a = new BoardItem(Guid.NewGuid(), "A", SortOrder: 1.0);
        var b = new BoardItem(Guid.NewGuid(), "B", SortOrder: 3.0);
        var c = new BoardItem(Guid.NewGuid(), "C", SortOrder: 5.0);
        var list = new List<BoardItem> { a, b, c };

        var mid = BoardItemReorder.ComputeMidpointSortOrder(list, 1, x => x.SortOrder);
        Assert.Equal(3.0, mid);
    }

    [Fact]
    public void ComputeMidpointSortOrder_with_null_neighbour_orders_uses_list_index()
    {
        var a = new BoardItem(Guid.NewGuid(), "A");
        var b = new BoardItem(Guid.NewGuid(), "B");
        var list = new List<BoardItem> { b, a };

        var mid = BoardItemReorder.ComputeMidpointSortOrder(list, 0, _ => null);

        Assert.Equal(0.0, mid);
    }

    [Fact]
    public void ComputeMidpointSortOrder_at_start_uses_next_minus_one()
    {
        var a = new BoardItem(Guid.NewGuid(), "A", SortOrder: 10.0);
        var b = new BoardItem(Guid.NewGuid(), "B", SortOrder: 20.0);
        var list = new List<BoardItem> { a, b };

        var mid = BoardItemReorder.ComputeMidpointSortOrder(list, 0, x => x.SortOrder);
        Assert.Equal(19.0, mid);
    }
}
