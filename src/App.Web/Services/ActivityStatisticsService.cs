using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class ActivityStatisticsService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IUserTimeZoneService _timeZone;

    public ActivityStatisticsService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IUserTimeZoneService timeZone)
    {
        _dbFactory = dbFactory;
        _timeZone = timeZone;
    }

    private DateOnly Today() => DailySchedule.LocalToday(_timeZone);

    public async Task<ActivityDayDetailDto> GetActivityDayDetailAsync(
        Guid userId,
        DateOnly day,
        string? tag,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var allowedIds = await GetBoardItemIdsMatchingTagOrNullAsync(db, userId, tag, cancellationToken);

        IQueryable<UserActivityEventEntity> ev = db.UserActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc);
        if (allowedIds is not null)
            ev = ev.Where(e => e.BoardItemId != null && allowedIds.Contains(e.BoardItemId.Value));

        var rows = await ev
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds, e.CustomLabel))
            .ToListAsync(cancellationToken);

        IReadOnlyList<Guid> boardIds = rows
            .Select(x => x.BoardItemId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        IReadOnlyDictionary<Guid, string> titles = boardIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.BoardItems.AsNoTracking()
                .Where(b => b.UserId == userId && boardIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Title, cancellationToken);

        return ActivityStatisticsCalculator.BuildDayDetail(day, rows, titles);
    }

    public async Task<ActivityDashboardDto> GetDashboardAsync(
        Guid userId,
        string? periodKey,
        string? tag,
        CancellationToken cancellationToken = default)
    {
        var utcToday = Today();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var options = await BuildDailyPeriodOptionsAsync(db, userId, utcToday, cancellationToken);
        var (key, start, end) = ActivityStatisticsCalculator.ResolveActivityPeriod(periodKey, utcToday, options);

        var fromUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var availableTags = await GetDistinctTagsForUserAsync(db, userId, cancellationToken);
        var allowedIds = await GetBoardItemIdsMatchingTagOrNullAsync(db, userId, tag, cancellationToken);

        IQueryable<UserActivityEventEntity> evq = db.UserActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc);
        if (allowedIds is not null)
            evq = evq.Where(e => e.BoardItemId != null && allowedIds.Contains(e.BoardItemId.Value));

        var rows = await evq
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds, e.CustomLabel))
            .ToListAsync(cancellationToken);

        var built = ActivityStatisticsCalculator.BuildDashboard(rows, key, start, end, utcToday);
        return built with { AvailableTags = availableTags };
    }

    public async Task<DailyContributionsViewDto> GetDailyContributionsAsync(
        Guid userId,
        string? periodKey,
        string? tag,
        CancellationToken cancellationToken = default)
    {
        var utcToday = Today();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var options = await BuildDailyPeriodOptionsAsync(db, userId, utcToday, cancellationToken);
        var (key, rangeStart, rangeEnd) =
            ActivityStatisticsCalculator.ResolveActivityPeriod(periodKey, utcToday, options);

        var fromUtc = rangeStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = rangeEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var allowedIds = await GetBoardItemIdsMatchingTagOrNullAsync(db, userId, tag, cancellationToken);

        IQueryable<BoardItemEntity> dailyQ = db.BoardItems.AsNoTracking()
            .Where(b => b.UserId == userId && b.DeletedAtUtc == null && b.Section == BoardSection.Daily);
        if (allowedIds is not null)
            dailyQ = dailyQ.Where(b => allowedIds.Contains(b.Id));

        var dailyItemRawRows = await dailyQ
            .OrderBy(b => b.Title)
            .Select(b => new { b.Id, b.Title, b.DailyStartDate, b.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var dailyItemRows = dailyItemRawRows.Select(b => new DailyItemStatsDto(
            b.Id,
            b.Title,
            b.DailyStartDate != null ? DateOnly.FromDateTime(b.DailyStartDate.Value) : null,
            DateOnly.FromDateTime(_timeZone.ConvertToLocal(b.CreatedAtUtc).DateTime)
        )).ToList();

        IQueryable<UserActivityEventEntity> evQ = db.UserActivityEvents.AsNoTracking()
            .Where(e =>
                e.UserId == userId &&
                e.OccurredAtUtc >= fromUtc &&
                e.OccurredAtUtc < toUtc);
        if (allowedIds is not null)
            evQ = evQ.Where(e => e.BoardItemId != null && allowedIds.Contains(e.BoardItemId.Value));

        var eventRows = await evQ
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds, e.CustomLabel))
            .ToListAsync(cancellationToken);

        return ActivityStatisticsCalculator.BuildDailyContributions(
            eventRows,
            dailyItemRows,
            key,
            options,
            rangeStart,
            rangeEnd,
            utcToday);
    }

    private async Task<IReadOnlyList<string>> GetDistinctTagsForUserAsync(
        ApplicationDbContext db,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var tagStrings = await db.BoardItems.AsNoTracking()
            .Where(b => b.UserId == userId && b.DeletedAtUtc == null)
            .Select(b => b.Tags)
            .ToListAsync(cancellationToken);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ts in tagStrings)
        foreach (var t in BoardTagUtil.ParseTags(ts))
            set.Add(t);

        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<HashSet<Guid>?> GetBoardItemIdsMatchingTagOrNullAsync(
        ApplicationDbContext db,
        Guid userId,
        string? tag,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var wantedTags = tag.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (wantedTags.Length == 0) return null;

        var wantedSet = new HashSet<string>(wantedTags, StringComparer.OrdinalIgnoreCase);

        var rows = await db.BoardItems.AsNoTracking()
            .Where(b => b.UserId == userId && b.DeletedAtUtc == null)
            .Select(b => new { b.Id, b.Tags })
            .ToListAsync(cancellationToken);

        var set = new HashSet<Guid>();
        foreach (var r in rows)
        {
            if (BoardTagUtil.ParseTags(r.Tags).Any(t => wantedSet.Contains(t)))
                set.Add(r.Id);
        }

        return set;
    }

    private async Task<IReadOnlyList<DailyGraphPeriodOption>> BuildDailyPeriodOptionsAsync(
        ApplicationDbContext db,
        Guid userId,
        DateOnly utcToday,
        CancellationToken cancellationToken)
    {
        var maxYear = utcToday.Year;
        var first = await db.UserActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.EventType == ActivityEventType.DailyComplete)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => (DateTimeOffset?)e.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var minYear = first is { } f ? f.UtcDateTime.Year : maxYear;
        if (minYear > maxYear) minYear = maxYear;

        var list = new List<DailyGraphPeriodOption>
        {
            new(DailyGraphPeriods.Rolling370Days, "Last 370 days")
        };
        for (var y = maxYear; y >= minYear; y--)
            list.Add(new DailyGraphPeriodOption(DailyGraphPeriods.ForCalendarYear(y), y.ToString()));

        return list;
    }
}
