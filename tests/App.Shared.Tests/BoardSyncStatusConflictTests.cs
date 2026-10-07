using App.Shared.RCL.Services.Board.Local;

namespace App.Shared.Tests;

public sealed class BoardSyncStatusConflictTests
{
    [Fact]
    public void RecordConflict_KeepsNewestFirst_AndCapsAtTwenty()
    {
        var status = new BoardSyncStatus();

        for (var i = 0; i < 25; i++)
        {
            status.RecordConflict(new BoardSyncConflict(
                DateTimeOffset.UtcNow,
                Guid.NewGuid(),
                $"Item {i}",
                BoardSyncConflictOutcome.KeptServer,
                "title"));
        }

        var recent = status.RecentConflicts;
        Assert.Equal(20, recent.Count);
        Assert.Equal("Item 24", recent[0].Title);
        Assert.Equal("Item 5", recent[19].Title);
    }

    [Fact]
    public void ClearSyncState_ClearsConflicts()
    {
        var status = new BoardSyncStatus();
        status.RecordConflict(new BoardSyncConflict(
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "Run",
            BoardSyncConflictOutcome.KeptDevice,
            "counter"));

        status.ClearSyncState();

        Assert.Empty(status.RecentConflicts);
    }
}
