using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

namespace App.MAUI.Services;

public sealed class MauiActivityStatisticsReader : IActivityStatisticsReader
{
    private readonly MauiActivityEventStore _store;
    private readonly IBoardDataService _board;

    public MauiActivityStatisticsReader(MauiActivityEventStore store, IBoardDataService board)
    {
        _store = store;
        _board = board;
    }

    public async Task<ActivityDashboardDto> GetDashboardAsync(string? periodKey, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StoredUserActivityEvent> all = await _store.GetAllAsync(cancellationToken);
        List<UserActivityEventRecord> userEvents = FilterUser(all);
        DateOnly utcToday = DailySchedule.UtcToday;
        IReadOnlyList<DailyGraphPeriodOption> options = ActivityStatisticsCalculator.BuildPeriodOptions(utcToday, userEvents);
        (string key, DateOnly start, DateOnly end) = ActivityStatisticsCalculator.ResolveActivityPeriod(periodKey, utcToday, options);
        List<UserActivityEventRecord> inRange = FilterRange(userEvents, start, end);
        return ActivityStatisticsCalculator.BuildDashboard(inRange, key, start, end, utcToday);
    }

    public async Task<DailyContributionsViewDto> GetDailyContributionsAsync(string? periodKey, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StoredUserActivityEvent> all = await _store.GetAllAsync(cancellationToken);
        List<UserActivityEventRecord> userEvents = FilterUser(all);
        DateOnly utcToday = DailySchedule.UtcToday;
        IReadOnlyList<DailyGraphPeriodOption> options = ActivityStatisticsCalculator.BuildPeriodOptions(utcToday, userEvents);
        (string key, DateOnly rangeStart, DateOnly rangeEnd) = ActivityStatisticsCalculator.ResolveActivityPeriod(periodKey, utcToday, options);
        List<UserActivityEventRecord> inRange = FilterRange(userEvents, rangeStart, rangeEnd);

        BoardSnapshot snap = await _board.GetSnapshotAsync(cancellationToken);
        IReadOnlyList<(Guid Id, string Title)> dailies = snap.Dailies
            .OrderBy(d => d.Title, StringComparer.Ordinal)
            .Select(d => (d.Id, d.Title))
            .ToList();

        return ActivityStatisticsCalculator.BuildDailyContributions(
            inRange,
            dailies,
            key,
            options,
            rangeStart,
            rangeEnd,
            utcToday);
    }

    public async Task<ActivityDayDetailDto> GetActivityDayDetailAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StoredUserActivityEvent> all = await _store.GetAllAsync(cancellationToken);
        var fromUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        List<UserActivityEventRecord> rows = all
            .Where(e => e.UserId == MauiLocalUser.Id && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds))
            .ToList();

        IReadOnlyDictionary<Guid, string> titles = await BuildTitleMapAsync(cancellationToken);
        return ActivityStatisticsCalculator.BuildDayDetail(day, rows, titles);
    }

    private List<UserActivityEventRecord> FilterUser(IReadOnlyList<StoredUserActivityEvent> all) =>
        all.Where(e => e.UserId == MauiLocalUser.Id)
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds))
            .ToList();

    private static List<UserActivityEventRecord> FilterRange(
        IReadOnlyList<UserActivityEventRecord> events,
        DateOnly start,
        DateOnly end)
    {
        var fromUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return events.Where(e => e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> BuildTitleMapAsync(CancellationToken cancellationToken)
    {
        BoardSnapshot snap = await _board.GetSnapshotAsync(cancellationToken);
        var map = new Dictionary<Guid, string>();
        foreach (BoardItem x in snap.Habits)
        {
            map[x.Id] = x.Title;
        }

        foreach (BoardItem x in snap.Dailies)
        {
            map[x.Id] = x.Title;
        }

        foreach (BoardItem x in snap.Todos)
        {
            map[x.Id] = x.Title;
        }

        return map;
    }
}
