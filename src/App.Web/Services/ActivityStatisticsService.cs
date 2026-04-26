using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class ActivityStatisticsService
{
    private readonly ApplicationDbContext _db;

    public ActivityStatisticsService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ActivityDayDetailDto> GetActivityDayDetailAsync(
        Guid userId,
        DateOnly day,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await _db.UserActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds))
            .ToListAsync(cancellationToken);

        IReadOnlyList<Guid> boardIds = rows
            .Select(x => x.BoardItemId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        IReadOnlyDictionary<Guid, string> titles = boardIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.BoardItems.AsNoTracking()
                .Where(b => b.UserId == userId && boardIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Title, cancellationToken);

        return ActivityStatisticsCalculator.BuildDayDetail(day, rows, titles);
    }

    public async Task<ActivityDashboardDto> GetDashboardAsync(
        Guid userId,
        string? periodKey,
        CancellationToken cancellationToken = default)
    {
        var utcToday = DailySchedule.UtcToday;
        var options = await BuildDailyPeriodOptionsAsync(userId, utcToday, cancellationToken);
        var (key, start, end) = ActivityStatisticsCalculator.ResolveActivityPeriod(periodKey, utcToday, options);

        var fromUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await _db.UserActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc)
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds))
            .ToListAsync(cancellationToken);

        return ActivityStatisticsCalculator.BuildDashboard(rows, key, start, end, utcToday);
    }

    public async Task<DailyContributionsViewDto> GetDailyContributionsAsync(
        Guid userId,
        string? periodKey,
        CancellationToken cancellationToken = default)
    {
        var utcToday = DailySchedule.UtcToday;
        var options = await BuildDailyPeriodOptionsAsync(userId, utcToday, cancellationToken);
        var (key, rangeStart, rangeEnd) =
            ActivityStatisticsCalculator.ResolveActivityPeriod(periodKey, utcToday, options);

        var fromUtc = rangeStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = rangeEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var dailyItemRows = await _db.BoardItems.AsNoTracking()
            .Where(b => b.UserId == userId && b.Section == BoardSection.Daily)
            .OrderBy(b => b.Title)
            .Select(b => new { b.Id, b.Title })
            .ToListAsync(cancellationToken);

        var eventRows = await _db.UserActivityEvents.AsNoTracking()
            .Where(e =>
                e.UserId == userId &&
                e.OccurredAtUtc >= fromUtc &&
                e.OccurredAtUtc < toUtc)
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds))
            .ToListAsync(cancellationToken);

        IReadOnlyList<(Guid Id, string Title)> dailies = dailyItemRows.Select(x => (x.Id, x.Title)).ToList();

        return ActivityStatisticsCalculator.BuildDailyContributions(
            eventRows,
            dailies,
            key,
            options,
            rangeStart,
            rangeEnd,
            utcToday);
    }

    private async Task<IReadOnlyList<DailyGraphPeriodOption>> BuildDailyPeriodOptionsAsync(
        Guid userId,
        DateOnly utcToday,
        CancellationToken cancellationToken)
    {
        var maxYear = utcToday.Year;
        var first = await _db.UserActivityEvents.AsNoTracking()
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
