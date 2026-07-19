using System.Collections.Frozen;
using System.Globalization;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class ActivityStatisticsService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IUserTimeZoneService timeZone,
    ActivityStatisticsCache cache)
{
    private DateOnly Today() => DailySchedule.LocalToday(timeZone);

    public async Task<ActivityDayDetailDto> GetActivityDayDetailAsync(
        Guid userId,
        DateOnly day,
        string? tag,
        CancellationToken cancellationToken = default)
    {
        ActivityStatisticsCache.UserCache userCache = cache.GetOrCreate(userId);
        (DateOnly, string?) cacheKey = (day, tag);
        if (userCache.DayDetail.TryGetValue(cacheKey, out ActivityDayDetailDto? cached))
        {
            return cached;
        }

        DateTime fromUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        DateTime toUtc = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        await using ApplicationDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        HashSet<Guid>? allowedIds = await GetBoardItemIdsMatchingTagOrNullAsync(db, userId, tag, cancellationToken);

        IQueryable<UserActivityEventEntity> ev = db.UserActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc);
        if (allowedIds is not null)
        {
            ev = ev.Where(e => e.BoardItemId != null && allowedIds.Contains(e.BoardItemId.Value));
        }

        List<UserActivityEventRecord> rows = await ev
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds, e.CustomLabel))
            .ToListAsync(cancellationToken);

        List<Guid> boardIds = [.. rows
            .Select(x => x.BoardItemId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()];

        IReadOnlyDictionary<Guid, string> titles = boardIds.Count == 0
            ? FrozenDictionary<Guid, string>.Empty
            : await db.BoardItems.AsNoTracking()
                .Where(b => b.UserId == userId && boardIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Title, cancellationToken);

        ActivityDayDetailDto result = ActivityStatisticsCalculator.BuildDayDetail(day, rows, titles);
        userCache.DayDetail[cacheKey] = result;
        return result;
    }

    public async Task<ActivityDashboardDto> GetDashboardAsync(
        Guid userId,
        string? periodKey,
        string? tag,
        CancellationToken cancellationToken = default)
    {
        DateOnly utcToday = Today();
        ActivityStatisticsCache.UserCache userCache = cache.GetOrCreate(userId);
        (string?, string?, DateOnly) cacheKey = (periodKey, tag, utcToday);
        if (userCache.Dashboard.TryGetValue(cacheKey, out ActivityDashboardDto? cached))
        {
            return cached;
        }

        await using ApplicationDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        IReadOnlyList<DailyGraphPeriodOption> options = await BuildDailyPeriodOptionsAsync(db, userId, utcToday, cancellationToken);
        (string key, DateOnly start, DateOnly end) = ActivityStatisticsCalculator.ResolveActivityPeriod(periodKey, utcToday, options);

        DateTime fromUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        DateTime toUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        IReadOnlyList<string> availableTags = await GetDistinctTagsForUserAsync(db, userId, cancellationToken);
        HashSet<Guid>? allowedIds = await GetBoardItemIdsMatchingTagOrNullAsync(db, userId, tag, cancellationToken);

        IQueryable<UserActivityEventEntity> evq = db.UserActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc);
        if (allowedIds is not null)
        {
            evq = evq.Where(e => e.BoardItemId != null && allowedIds.Contains(e.BoardItemId.Value));
        }

        List<UserActivityEventRecord> rows = await evq
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds, e.CustomLabel))
            .ToListAsync(cancellationToken);

        ActivityDashboardDto built = ActivityStatisticsCalculator.BuildDashboard(rows, key, start, end, utcToday);
        ActivityDashboardDto result = built with { AvailableTags = availableTags };
        userCache.Dashboard[cacheKey] = result;
        return result;
    }

    public async Task<DailyContributionsViewDto> GetDailyContributionsAsync(
        Guid userId,
        string? periodKey,
        string? tag,
        CancellationToken cancellationToken = default)
    {
        DateOnly utcToday = Today();
        ActivityStatisticsCache.UserCache userCache = cache.GetOrCreate(userId);
        (string?, string?, DateOnly) cacheKey = (periodKey, tag, utcToday);
        if (userCache.DailyContributions.TryGetValue(cacheKey, out DailyContributionsViewDto? cached))
        {
            return cached;
        }

        await using ApplicationDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        IReadOnlyList<DailyGraphPeriodOption> options = await BuildDailyPeriodOptionsAsync(db, userId, utcToday, cancellationToken);
        (string key, DateOnly rangeStart, DateOnly rangeEnd) =
            ActivityStatisticsCalculator.ResolveActivityPeriod(periodKey, utcToday, options);

        DateTime fromUtc = rangeStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        DateTime toUtc = rangeEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        HashSet<Guid>? allowedIds = await GetBoardItemIdsMatchingTagOrNullAsync(db, userId, tag, cancellationToken);

        IQueryable<BoardItemEntity> dailyQ = db.BoardItems.AsNoTracking()
            .Where(b => b.UserId == userId && b.DeletedAtUtc == null && b.Section == BoardSection.Daily);
        if (allowedIds is not null)
        {
            dailyQ = dailyQ.Where(b => allowedIds.Contains(b.Id));
        }

        var dailyItemRawRows = await dailyQ
            .OrderBy(b => b.Title)
            .Select(b => new { b.Id, b.Title, b.DailyStartDate, b.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        List<DailyItemStatsDto> dailyItemRows = [.. dailyItemRawRows.Select(b => new DailyItemStatsDto(
            b.Id,
            b.Title,
            b.DailyStartDate != null ? DateOnly.FromDateTime(b.DailyStartDate.Value) : null,
            DateOnly.FromDateTime(timeZone.ConvertToLocal(b.CreatedAtUtc).DateTime)
        ))];

        IQueryable<UserActivityEventEntity> evQ = db.UserActivityEvents.AsNoTracking()
            .Where(e =>
                e.UserId == userId &&
                e.OccurredAtUtc >= fromUtc &&
                e.OccurredAtUtc < toUtc);
        if (allowedIds is not null)
        {
            evQ = evQ.Where(e => e.BoardItemId != null && allowedIds.Contains(e.BoardItemId.Value));
        }

        List<UserActivityEventRecord> eventRows = await evQ
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds, e.CustomLabel))
            .ToListAsync(cancellationToken);

        DailyContributionsViewDto result = ActivityStatisticsCalculator.BuildDailyContributions(
            eventRows,
            dailyItemRows,
            key,
            options,
            rangeStart,
            rangeEnd,
            utcToday);

        userCache.DailyContributions[cacheKey] = result;
        return result;
    }

    private static async Task<IReadOnlyList<string>> GetDistinctTagsForUserAsync(
        ApplicationDbContext db,
        Guid userId,
        CancellationToken cancellationToken)
    {
        List<string?> tagStrings = await db.BoardItems.AsNoTracking()
            .Where(b => b.UserId == userId && b.DeletedAtUtc == null)
            .Select(b => b.Tags)
            .ToListAsync(cancellationToken);

        HashSet<string> set = new(
            tagStrings.SelectMany(BoardTagUtil.ParseTags),
            StringComparer.OrdinalIgnoreCase);

        return [.. set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
    }

    private static async Task<HashSet<Guid>?> GetBoardItemIdsMatchingTagOrNullAsync(
        ApplicationDbContext db,
        Guid userId,
        string? tag,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        string[] wantedTags = tag.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (wantedTags.Length == 0)
        {
            return null;
        }

        if (wantedTags.Length == 1)
        {
            var t = wantedTags[0].Trim();
            if (t.Length == 0)
            {
                return null;
            }

            var ids = await db.BoardItems.AsNoTracking()
                .Where(b => b.UserId == userId && b.DeletedAtUtc == null
                    && b.Tags != null
                    && (b.Tags == t
                        || b.Tags.StartsWith(t + ",")
                        || b.Tags.Contains("," + t + ",")
                        || b.Tags.EndsWith("," + t)))
                .Select(b => b.Id)
                .ToListAsync(cancellationToken);

            return [.. ids];
        }

        HashSet<string> wantedSet = new(wantedTags, StringComparer.OrdinalIgnoreCase);

        var rows = await db.BoardItems.AsNoTracking()
            .Where(b => b.UserId == userId && b.DeletedAtUtc == null)
            .Select(b => new { b.Id, b.Tags })
            .ToListAsync(cancellationToken);

        return [.. rows
            .Where(r => BoardTagUtil.ParseTags(r.Tags).Any(t => wantedSet.Contains(t)))
            .Select(r => r.Id)];
    }

    private static async Task<IReadOnlyList<DailyGraphPeriodOption>> BuildDailyPeriodOptionsAsync(
        ApplicationDbContext db,
        Guid userId,
        DateOnly utcToday,
        CancellationToken cancellationToken)
    {
        int maxYear = utcToday.Year;
        var firstEvent = await db.UserActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.EventType == ActivityEventType.DailyComplete)
            .MinByAsync(e => e.OccurredAtUtc, cancellationToken);

        int minYear = firstEvent is { } f ? f.OccurredAtUtc.UtcDateTime.Year : maxYear;
        if (minYear > maxYear)
        {
            minYear = maxYear;
        }

        List<DailyGraphPeriodOption> list =
        [
            new(DailyGraphPeriods.Rolling370Days, "Last 370 days")
        ];
        for (int y = maxYear; y >= minYear; y--)
        {
            list.Add(new DailyGraphPeriodOption(DailyGraphPeriods.ForCalendarYear(y), y.ToString(CultureInfo.InvariantCulture)));
        }

        return list;
    }
}
