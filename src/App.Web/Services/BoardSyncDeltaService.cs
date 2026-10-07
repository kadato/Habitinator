using System.Globalization;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

/// <summary>Paged incremental board changes for sync clients.</summary>
public sealed class BoardSyncDeltaService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ApplicationDbContext dbContext,
    IUserTimeZoneService timeZone,
    DailyStreakCalculationService streakCalculator)
{
    public const int SyncDefaultPageSize = 1000;
    public const int SyncMaxPageSize = 5000;

    public async Task<BoardSyncDelta> GetSyncDeltaAsync(
        Guid userId,
        DateTimeOffset cursorExclusive,
        CancellationToken cancellationToken = default)
    {
        return await GetSyncDeltaAsync(userId, cursorExclusive.ToString("O"), null, cancellationToken);
    }

    /// <summary>
    /// Paged incremental changes. <paramref name="cursorRaw"/> is either a legacy ISO-8601
    /// watermark or a <c>watermark|id</c> composite returned for a truncated page.
    /// Pages hold at most <paramref name="limitRaw"/> rows (default 1000, max 5000).
    /// A truncated page returns a composite cursor; a complete page returns a plain watermark.
    /// </summary>
    public async Task<BoardSyncDelta> GetSyncDeltaAsync(
        Guid userId,
        string cursorRaw,
        int? limitRaw,
        CancellationToken cancellationToken = default)
    {
        var (cursorTs, cursorId) = ParseSyncCursor(cursorRaw);
        var limit = Math.Clamp(limitRaw ?? SyncDefaultPageSize, 1, SyncMaxPageSize);

        using var activity = AppTelemetry.Activity.StartActivity("board.sync_delta");
        await using var readDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var newer = await FetchNewerRowsAsync(readDb, userId, cursorTs, limit, cancellationToken);
        var boundary = await FetchBoundaryRowsAsync(readDb, userId, cursorTs, cursorId, limit, cancellationToken);
        var (changed, truncated) = BuildChangedPage(newer, boundary, limit);

        var (today, dayStart) = await TodayAndDayStartAsync(userId, cancellationToken);
        var dailyRows = changed.Where(x => x.DeletedAtUtc is null && x.Section == BoardSection.Daily).ToList();
        var dailyStreaks = await streakCalculator.BuildDailyStreakMapAsync(userId, dailyRows, today, dayStart, readDb, cancellationToken);

        var (upserts, deletedIds, next) = PartitionSyncRows(changed, today, dailyStreaks);
        var nextCursor = ResolveNextCursor(truncated, next, cursorTs, changed);

        AppTelemetry.RecordSyncDelta(upserts.Count, deletedIds.Count);
        return new BoardSyncDelta(upserts, deletedIds, nextCursor);
    }

    private Task<(DateOnly Today, TimeSpan? DayStartLocalTime)> TodayAndDayStartAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        UserDayContext.LoadAsync(dbContext, userId, timeZone, cancellationToken);

    private static async Task<List<BoardItemEntity>> FetchNewerRowsAsync(
        ApplicationDbContext readDb,
        Guid userId,
        DateTimeOffset cursorTs,
        int limit,
        CancellationToken cancellationToken)
    {
        // Watermark per row: deletion time for tombstones, update time otherwise.
        // Same-watermark rows are resolved in memory by id so no Guid ordering
        // comparison is pushed to the database provider.
        return await readDb.BoardItems
            .AsNoTracking()
            .Where(x => x.UserId == userId
                        && (x.DeletedAtUtc != null ? x.DeletedAtUtc.Value : x.UpdatedAtUtc) > cursorTs)
            .OrderBy(x => x.DeletedAtUtc != null ? x.DeletedAtUtc.Value : x.UpdatedAtUtc)
            .ThenBy(x => x.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
    }

    private static async Task<List<BoardItemEntity>> FetchBoundaryRowsAsync(
        ApplicationDbContext readDb,
        Guid userId,
        DateTimeOffset cursorTs,
        Guid? cursorId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (cursorId is not { } lastId)
        {
            return [];
        }

        // Same-watermark rows share one timestamp, usually from a bulk rebalance.
        // Cap the scan so one skewed watermark cannot pull the whole table.
        var take = Math.Clamp(limit + 1, 1, SyncMaxPageSize + 1);
        var sameWatermark = await readDb.BoardItems
            .AsNoTracking()
            .Where(x => x.UserId == userId
                        && (x.DeletedAtUtc != null ? x.DeletedAtUtc.Value : x.UpdatedAtUtc) == cursorTs)
            .OrderBy(x => x.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        return sameWatermark
            .Where(x => x.Id.CompareTo(lastId) > 0)
            .ToList();
    }

    private static (List<BoardItemEntity> Changed, bool Truncated) BuildChangedPage(
        List<BoardItemEntity> newer,
        List<BoardItemEntity> boundary,
        int limit)
    {
        var merged = newer
            .Concat(boundary)
            .OrderBy(static x => x.DeletedAtUtc ?? x.UpdatedAtUtc)
            .ThenBy(static x => x.Id)
            .Take(limit + 1)
            .ToList();
        var truncated = merged.Count > limit;
        var changed = truncated ? merged.Take(limit).ToList() : merged;
        return (changed, truncated);
    }

    private static (List<BoardSyncItem> Upserts, List<Guid> DeletedIds, DateTimeOffset? Next) PartitionSyncRows(
        List<BoardItemEntity> changed,
        DateOnly today,
        IReadOnlyDictionary<Guid, int> dailyStreaks)
    {
        var upserts = new List<BoardSyncItem>();
        var deletedIds = new List<Guid>();
        DateTimeOffset? next = null;

        foreach (var row in changed)
        {
            if (row.DeletedAtUtc is not null)
            {
                deletedIds.Add(row.Id);
                next = MaxCursor(next, row.DeletedAtUtc.Value);
                continue;
            }

            upserts.Add(new BoardSyncItem(row.Section, BoardPersistenceService.ToModelWithToday(row, today, dailyStreaks)));
            next = MaxCursor(next, row.UpdatedAtUtc);
        }

        return (upserts, deletedIds, next);
    }

    private static string ResolveNextCursor(
        bool truncated,
        DateTimeOffset? next,
        DateTimeOffset cursorTs,
        List<BoardItemEntity> changed)
    {
        if (!truncated)
        {
            return (next ?? cursorTs).ToString("O");
        }

        var last = changed[^1];
        var lastWatermark = last.DeletedAtUtc ?? last.UpdatedAtUtc;
        return $"{lastWatermark:O}|{last.Id:D}";
    }

    private static (DateTimeOffset Watermark, Guid? LastId) ParseSyncCursor(string cursorRaw)
    {
        var separator = cursorRaw.IndexOf('|');
        if (separator < 0)
        {
            return (DateTimeOffset.Parse(cursorRaw, CultureInfo.InvariantCulture), null);
        }

        var watermark = DateTimeOffset.Parse(cursorRaw[..separator], CultureInfo.InvariantCulture);
        var lastId = Guid.Parse(cursorRaw[(separator + 1)..]);
        return (watermark, lastId);
    }

    private static DateTimeOffset? MaxCursor(DateTimeOffset? a, DateTimeOffset b) => a is null || b > a ? b : a;
}
