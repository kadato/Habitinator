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
        var userCache = cache.GetOrCreate(userId);
        (DateOnly, string?) cacheKey = (day, tag);
        if (userCache.DayDetail.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var fromUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var allowedIds = await GetBoardItemIdsMatchingTagOrNullAsync(db, userId, tag, cancellationToken);

        var rows = await EventsInRange(db, userId, fromUtc, toUtc, allowedIds)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds, e.CustomLabel))
            .ToListAsync(cancellationToken);

        List<Guid> boardIds = [.. rows
            .Select(x => x.BoardItemId)
            .OfType<Guid>()
            .Distinct()];

        IReadOnlyDictionary<Guid, string> titles = boardIds.Count == 0
            ? FrozenDictionary<Guid, string>.Empty
            : await db.BoardItems.AsNoTracking()
                .Where(b => b.UserId == userId && boardIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Title, cancellationToken);

        var result = ActivityStatisticsCalculator.BuildDayDetail(day, rows, titles);
        userCache.DayDetail.Set(cacheKey, result);
        return result;
    }

    public async Task<ActivityDashboardDto> GetDashboardAsync(
        Guid userId,
        string? periodKey,
        string? tag,
        CancellationToken cancellationToken = default)
    {
        var utcToday = Today();
        var userCache = cache.GetOrCreate(userId);
        (string?, string?, DateOnly) cacheKey = (periodKey, tag, utcToday);
        if (userCache.Dashboard.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var options = await BuildDailyPeriodOptionsAsync(db, userId, utcToday, cancellationToken);
        (var key, var start, var end) = ActivityStatisticsCalculator.ResolveActivityPeriod(periodKey, utcToday, options);

        var fromUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var availableTags = await GetDistinctTagsForUserAsync(db, userId, cancellationToken);
        var allowedIds = await GetBoardItemIdsMatchingTagOrNullAsync(db, userId, tag, cancellationToken);

        var rows = await EventsInRange(db, userId, fromUtc, toUtc, allowedIds)
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds, e.CustomLabel))
            .ToListAsync(cancellationToken);

        var built = ActivityStatisticsCalculator.BuildDashboard(rows, key, start, end, utcToday);
        var result = built with { AvailableTags = availableTags };
        userCache.Dashboard.Set(cacheKey, result);
        return result;
    }

    public async Task<DailyContributionsViewDto> GetDailyContributionsAsync(
        Guid userId,
        string? periodKey,
        string? tag,
        CancellationToken cancellationToken = default)
    {
        var utcToday = Today();
        var userCache = cache.GetOrCreate(userId);
        (string?, string?, DateOnly) cacheKey = (periodKey, tag, utcToday);
        if (userCache.DailyContributions.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ctx = await LoadContributionsQueryAsync(db, userId, BoardSection.Daily, periodKey, tag, utcToday, cancellationToken);

        List<DailyItemStatsDto> dailyItemRows = [.. ctx.ItemRows.Select(b => new DailyItemStatsDto(
            b.Id,
            b.Title,
            b.DailyStartDate != null ? DateOnly.FromDateTime(b.DailyStartDate.Value) : null,
            DateOnly.FromDateTime(timeZone.ConvertToLocal(b.CreatedAtUtc).DateTime)
        ))];

        var result = ActivityStatisticsCalculator.BuildDailyContributions(
            ctx.EventRows,
            dailyItemRows,
            ctx.Key,
            ctx.Options,
            ctx.RangeStart,
            ctx.RangeEnd,
            ctx.UtcToday);

        userCache.DailyContributions.Set(cacheKey, result);
        return result;
    }

    public async Task<HabitContributionsViewDto> GetHabitContributionsAsync(
        Guid userId,
        string? periodKey,
        string? tag,
        CancellationToken cancellationToken = default)
    {
        var utcToday = Today();
        var userCache = cache.GetOrCreate(userId);
        (string?, string?, DateOnly) cacheKey = (periodKey, tag, utcToday);
        if (userCache.HabitContributions.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ctx = await LoadContributionsQueryAsync(db, userId, BoardSection.Habit, periodKey, tag, utcToday, cancellationToken);

        var result = ActivityStatisticsCalculator.BuildHabitContributions(
            ctx.EventRows,
            [.. ctx.ItemRows.Select(b => new HabitItemStatsDto(
                b.Id,
                b.Title,
                DateOnly.FromDateTime(timeZone.ConvertToLocal(b.CreatedAtUtc).DateTime)))],
            ctx.Key,
            ctx.Options,
            ctx.RangeStart,
            ctx.RangeEnd,
            ctx.UtcToday);

        userCache.HabitContributions.Set(cacheKey, result);
        return result;
    }

    private static IQueryable<UserActivityEventEntity> EventsInRange(
        ApplicationDbContext db,
        Guid userId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        HashSet<Guid>? allowedIds)
    {
        var ev = db.UserActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc);
        if (allowedIds is not null)
        {
            ev = ev.Where(e => e.BoardItemId != null && allowedIds.Contains(e.BoardItemId.Value));
        }

        return ev;
    }

    private static async Task<ContributionsQueryContext> LoadContributionsQueryAsync(
        ApplicationDbContext db,
        Guid userId,
        BoardSection section,
        string? periodKey,
        string? tag,
        DateOnly utcToday,
        CancellationToken cancellationToken)
    {
        var options = await BuildDailyPeriodOptionsAsync(db, userId, utcToday, cancellationToken);
        (var key, var rangeStart, var rangeEnd) =
            ActivityStatisticsCalculator.ResolveActivityPeriod(periodKey, utcToday, options);

        var fromUtc = rangeStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = rangeEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var allowedIds = await GetBoardItemIdsMatchingTagOrNullAsync(db, userId, tag, cancellationToken);

        var itemQ = db.BoardItems.AsNoTracking()
            .Where(b => b.UserId == userId && b.DeletedAtUtc == null && b.Section == section);
        if (allowedIds is not null)
        {
            itemQ = itemQ.Where(b => allowedIds.Contains(b.Id));
        }

        var itemRows = await itemQ
            .OrderBy(b => b.Title)
            .Select(b => new BoardItemRef(b.Id, b.Title, b.DailyStartDate, b.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var eventRows = await EventsInRange(db, userId, fromUtc, toUtc, allowedIds)
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds, e.CustomLabel))
            .ToListAsync(cancellationToken);

        return new ContributionsQueryContext(options, key, rangeStart, rangeEnd, utcToday, eventRows, itemRows);
    }

    private sealed record BoardItemRef(Guid Id, string Title, DateTime? DailyStartDate, DateTimeOffset CreatedAtUtc);

    private sealed record ContributionsQueryContext(
        IReadOnlyList<DailyGraphPeriodOption> Options,
        string Key,
        DateOnly RangeStart,
        DateOnly RangeEnd,
        DateOnly UtcToday,
        List<UserActivityEventRecord> EventRows,
        List<BoardItemRef> ItemRows);

    private static async Task<IReadOnlyList<string>> GetDistinctTagsForUserAsync(
        ApplicationDbContext db,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var tagStrings = await db.BoardItems.AsNoTracking()
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

        var wantedTags = tag.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
        var maxYear = utcToday.Year;
        var firstEvent = await db.UserActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.EventType == ActivityEventType.DailyComplete)
            .MinByAsync(e => e.OccurredAtUtc, cancellationToken);

        var minYear = firstEvent is { } f ? f.OccurredAtUtc.UtcDateTime.Year : maxYear;
        if (minYear > maxYear)
        {
            minYear = maxYear;
        }

        List<DailyGraphPeriodOption> list =
        [
            new(DailyGraphPeriods.Rolling370Days, "Last 370 days")
        ];
        for (var y = maxYear; y >= minYear; y--)
        {
            list.Add(new DailyGraphPeriodOption(DailyGraphPeriods.ForCalendarYear(y), y.ToString(CultureInfo.InvariantCulture)));
        }

        return list;
    }
}
