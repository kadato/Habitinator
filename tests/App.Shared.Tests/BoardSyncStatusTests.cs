using App.Shared.RCL.Services.Board.Local;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class BoardSyncStatusTests
{
    [Fact]
    public void SetPendingCount_ClampsNegativeToZero()
    {
        var status = new BoardSyncStatus();
        status.SetPendingCount(-4);
        status.PendingCount.Should().Be(0);
    }

    [Fact]
    public void SetPendingCount_RaisesChangedOnlyOnChange()
    {
        var status = new BoardSyncStatus();
        var raised = 0;
        status.Changed += (_, _) => raised++;

        status.SetPendingCount(3);
        status.PendingCount.Should().Be(3);
        status.SetPendingCount(3);
        raised.Should().Be(1);
    }

    [Fact]
    public void ClearSyncState_ResetsPendingCount()
    {
        var status = new BoardSyncStatus();
        status.SetPendingCount(5);
        status.ClearSyncState();
        status.PendingCount.Should().Be(0);
    }
}
