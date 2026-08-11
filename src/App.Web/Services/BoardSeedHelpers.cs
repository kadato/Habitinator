using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

internal static class BoardSeedHelpers
{
    public static async Task<bool> HasLiveBoardItemsAsync(
        ApplicationDbContext db,
        Guid userId,
        CancellationToken cancellationToken) =>
        await db.BoardItems.AnyAsync(
            x => x.UserId == userId && x.DeletedAtUtc == null && !x.IsArchived, cancellationToken);

    public static void AddBoardRow(
        ApplicationDbContext db,
        BoardItemEntity row,
        ref int order,
        DateTimeOffset utcNow)
    {
        var seq = order++;
        row.SortOrder = seq + 1.0;
        var t = utcNow.AddSeconds(seq);
        row.CreatedAtUtc = t;
        row.UpdatedAtUtc = t;
        db.BoardItems.Add(row);
    }
}
