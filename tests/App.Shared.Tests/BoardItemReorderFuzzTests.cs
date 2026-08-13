using App.Shared.RCL.Models;

using FsCheck;
using FsCheck.Xunit;

namespace App.Shared.Tests;

public sealed class BoardItemReorderFuzzTests
{
    [Property]
    public void ComputeMidpointSortOrder_IsStrictlyBetweenNeighbours(
        double? prevSortVal,
        double? nextSortVal,
        int insertAt)
    {
        // Ignore non-finite values, NaN and Infinity, or excessively large values that cause overflow
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
        var diff = Math.Abs(prevResolved - nextResolved);
        if (diff is >= double.Epsilon and < 1e-9)
        {
            return;
        }

        // We'll construct a mock list of 3 items to evaluate insert at index 1
        var itemLeft = new BoardItem(Guid.NewGuid(), "Left", SortOrder: prevSortVal);
        var itemRight = new BoardItem(Guid.NewGuid(), "Right", SortOrder: nextSortVal);

        // This is the list after insert but we evaluate neighbours at insertAt - 1 and insertAt + 1
        List<BoardItem> visibleList = [itemLeft, new BoardItem(Guid.NewGuid(), "Inserted"), itemRight];

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
