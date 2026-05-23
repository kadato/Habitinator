using App.Shared.RCL.Models;

namespace App.Shared.Tests;

public sealed class BoardItemReorderTests
{
    [Theory]
    [InlineData(0, 2, true, 1)]
    [InlineData(0, 2, false, 2)]
    [InlineData(2, 0, true, 0)]
    [InlineData(2, 0, false, 1)]
    public void ComputeInsertIndex_matches_drag_semantics(int source, int target, bool insertBefore, int expected)
    {
        var insertAt = BoardItemReorder.ComputeInsertIndex(source, target, insertBefore);
        Assert.Equal(expected, insertAt);
    }

    [Fact]
    public void ComputeMoveUpIndex_at_top_returns_null()
    {
        Assert.Null(BoardItemReorder.ComputeMoveUpIndex(0));
        Assert.Equal(1, BoardItemReorder.ComputeMoveUpIndex(2));
    }

    [Fact]
    public void ComputeMoveDownIndex_at_bottom_returns_null()
    {
        Assert.Null(BoardItemReorder.ComputeMoveDownIndex(2, 3));
        Assert.Equal(2, BoardItemReorder.ComputeMoveDownIndex(1, 3));
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

    [Fact]
    public void TryComputeReorder_moves_item_down_one_slot()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var items = new[]
        {
            new BoardItem(id1, "One", SortOrder: 1.0),
            new BoardItem(id2, "Two", SortOrder: 2.0),
            new BoardItem(id3, "Three", SortOrder: 3.0)
        };

        var result = BoardItemReorder.TryComputeReorder(items, id1, id2, insertBefore: false, x => x.SortOrder);

        Assert.NotNull(result);
        Assert.Equal([id2, id1, id3], result.Value.Reordered.Select(x => x.Id));
        Assert.Equal(2.5, result.Value.NewSortOrder);
    }
}
