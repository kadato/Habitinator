using App.Shared.RCL.Models;

using FsCheck;
using FsCheck.Xunit;

namespace App.Shared.Tests;

public sealed class BoardItemReorderFuzzTests
{
    [Property]
    public void ComputeInsertIndex_InBounds(int source, int target, bool insertBefore)
    {
        // Keep index sizes reasonable, e.g., simulating a list size up to 100
        var n = 100;
        var src = Math.Abs(source) % n;
        var tgt = Math.Abs(target) % n;

        var result = BoardItemReorder.ComputeInsertIndex(src, tgt, insertBefore);

        // Result index must always be within list bounds [0, n - 1]
        Assert.True(result >= 0);
        Assert.True(result < n);
    }

    [Property]
    public void ComputeMoveUpDown_NeverThrows(int source, int listCount)
    {
        BoardItemReorder.ComputeMoveUpIndex(source);
        BoardItemReorder.ComputeMoveDownIndex(source, listCount);
    }

    [Property]
    public void ComputeMoveUp_CorrectBehavior(int source)
    {
        var result = BoardItemReorder.ComputeMoveUpIndex(source);
        if (source > 0)
        {
            Assert.Equal(source - 1, result);
        }
        else
        {
            Assert.Null(result);
        }
    }

    [Property]
    public void ComputeMoveDown_CorrectBehavior(int source, int listCount)
    {
        // Avoid negative or zero listCount
        var count = Math.Max(1, listCount);
        var result = BoardItemReorder.ComputeMoveDownIndex(source, count);
        if (source < count - 1)
        {
            Assert.Equal(source + 1, result);
        }
        else
        {
            Assert.Null(result);
        }
    }

    [Property]
    public void ComputeMidpointSortOrder_IsStrictlyBetweenNeighbours(
        double? prevSortVal,
        double? nextSortVal,
        int insertAt)
    {
        // Ignore non-finite values (NaN, Infinity) or excessively large values that cause overflow
        if (prevSortVal.HasValue && (!double.IsFinite(prevSortVal.Value) || double.IsNaN(prevSortVal.Value) || Math.Abs(prevSortVal.Value) > 1e9))
        {
            return;
        }

        if (nextSortVal.HasValue && (!double.IsFinite(nextSortVal.Value) || double.IsNaN(nextSortVal.Value) || Math.Abs(nextSortVal.Value) > 1e9))
        {
            return;
        }

        // Resolve sort orders like in the implementation
        var prevResolved = prevSortVal ?? 0; // listIndex = 0
        var nextResolved = nextSortVal ?? 2; // listIndex = 2

        // Discard cases where they are distinct but so close that floating-point limits prevent finding a strict midpoint
        if (prevResolved != nextResolved && Math.Abs(prevResolved - nextResolved) < 1e-9)
        {
            return;
        }

        // We'll construct a mock list of 3 items to evaluate insert at index 1
        var itemLeft = new BoardItem(Guid.NewGuid(), "Left", SortOrder: prevSortVal);
        var itemRight = new BoardItem(Guid.NewGuid(), "Right", SortOrder: nextSortVal);

        // This is the list after insert but we evaluate neighbours at insertAt - 1 and insertAt + 1
        var visibleList = new List<BoardItem> { itemLeft, new BoardItem(Guid.NewGuid(), "Inserted"), itemRight };

        var mid = BoardItemReorder.ComputeMidpointSortOrder(visibleList, 1, x => x.SortOrder);

        if (prevResolved < nextResolved)
        {
            Assert.NotNull(mid);
            Assert.True(mid.Value > prevResolved);
            Assert.True(mid.Value < nextResolved);
        }
        else if (prevResolved > nextResolved)
        {
            Assert.NotNull(mid);
            Assert.True(mid.Value < prevResolved);
            Assert.True(mid.Value > nextResolved);
        }
        else // prevResolved == nextResolved
        {
            Assert.NotNull(mid);
            Assert.Equal(prevResolved, mid.Value);
        }
    }
}
