using System.Collections.Concurrent;

using App.Shared.RCL.Models;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

public sealed class OfflineActivityStatisticsProvider : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IActivityEventStore _eventStore;
    private readonly ILogger<OfflineActivityStatisticsProvider> _logger;
    private readonly ConcurrentDictionary<string, ActivityOverviewDto> _overviewCache = new();
    private readonly ConcurrentDictionary<string, int> _overviewEventCount = new();
    private readonly ConcurrentDictionary<string, int> _overviewBoardHash = new();

    public OfflineActivityStatisticsProvider(
        IServiceProvider serviceProvider,
        IActivityEventStore eventStore,
        ILogger<OfflineActivityStatisticsProvider> logger)
    {
        _serviceProvider = serviceProvider;
        _eventStore = eventStore;
        _logger = logger;
        _eventStore.Appended += OnEventAppended;
    }

    public void Dispose()
    {
        _eventStore.Appended -= OnEventAppended;
    }

    private void OnEventAppended(object? sender, UserActivityEventRecord record)
    {
        // Invalidate every cached overview. The range check that this replaced needed a synchronous
        // preference read on the event thread, which could deadlock the UI in WASM and MAUI. A
        // recompute is cheap next to serving statistics that no longer match the event log.
        _overviewCache.Clear();
        _overviewEventCount.Clear();
        _overviewBoardHash.Clear();
    }

    public async Task<ActivityOverviewDto> BuildOverviewAsync(string? periodKey, string? tag, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{periodKey}|{tag}";
        var snapshot = await GetBoardSnapshotAsync(cancellationToken);
        var allEvents = await _eventStore.GetAllAsync(cancellationToken);
        var boardHash = ComputeBoardHash(snapshot);
        var eventCount = allEvents.Count;

        if (_overviewCache.TryGetValue(cacheKey, out var cached) &&
            _overviewEventCount.TryGetValue(cacheKey, out var cachedCount) && cachedCount == eventCount &&
            _overviewBoardHash.TryGetValue(cacheKey, out var cachedHash) && cachedHash == boardHash)
        {
            return cached;
        }

        var (today, dayStart, timeZone) = await GetTodayAndDayStartAsync(cancellationToken);
        var periodOptions = ActivityStatisticsCalculator.BuildPeriodOptions(today, allEvents, timeZone, dayStart);
        var (key, start, end) = ActivityStatisticsCalculator.ResolveActivityPeriod(periodKey, today, periodOptions);

        var fromUtc = start.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = end.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var filteredEvents = FilterEvents(allEvents, fromUtc, toUtc, tag, snapshot);

        var availableTags = GetDistinctTags(snapshot);

        var dashboard = ActivityStatisticsCalculator.BuildDashboard(filteredEvents, key, start, end, today, timeZone, dayStart) with { AvailableTags = availableTags };

        var dailyItems = GetDailyItems(snapshot);
        var dailyResult = ActivityStatisticsCalculator.BuildDailyContributions(
            filteredEvents,
            dailyItems,
            new ContributionsRangeContext(key, periodOptions, start, end, today, timeZone, dayStart));

        var habitItems = GetHabitItems(snapshot);
        var habitResult = ActivityStatisticsCalculator.BuildHabitContributions(
            filteredEvents,
            habitItems,
            new ContributionsRangeContext(key, periodOptions, start, end, today, timeZone, dayStart));

        var overview = new ActivityOverviewDto(dashboard, dailyResult, habitResult);
        _overviewCache[cacheKey] = overview;
        _overviewEventCount[cacheKey] = eventCount;
        _overviewBoardHash[cacheKey] = boardHash;
        return overview;
    }

    public async Task<ActivityDashboardDto> BuildDashboardAsync(string? periodKey, string? tag, CancellationToken cancellationToken = default)
    {
        var overview = await BuildOverviewAsync(periodKey, tag, cancellationToken);
        return overview.Dashboard;
    }

    public async Task<DailyContributionsViewDto> BuildDailyContributionsAsync(string? periodKey, string? tag, CancellationToken cancellationToken = default)
    {
        var overview = await BuildOverviewAsync(periodKey, tag, cancellationToken);
        return overview.DailyContributions;
    }

    public async Task<HabitContributionsViewDto> BuildHabitContributionsAsync(string? periodKey, string? tag, CancellationToken cancellationToken = default)
    {
        var overview = await BuildOverviewAsync(periodKey, tag, cancellationToken);
        return overview.HabitContributions;
    }

    public async Task<ActivityDayDetailDto> BuildDayDetailAsync(DateOnly day, string? tag, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetBoardSnapshotAsync(cancellationToken);
        var allEvents = await _eventStore.GetAllAsync(cancellationToken);
        var (_, dayStart, timeZone) = await GetTodayAndDayStartAsync(cancellationToken);

        var fromUtc = day.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = day.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var filtered = FilterEvents(allEvents, fromUtc, toUtc, tag, snapshot)
            .Where(e => DailySchedule.LocalDay(e.OccurredAtUtc, timeZone, dayStart) == day)
            .ToList();

        var titles = snapshot.Habits.Concat(snapshot.Dailies).Concat(snapshot.Todos)
            .ToDictionary(b => b.Id, b => b.Title);

        return ActivityStatisticsCalculator.BuildDayDetail(day, filtered, titles, timeZone, dayStart);
    }

    private async Task<BoardSnapshot> GetBoardSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var boardData = scope.ServiceProvider.GetRequiredService<IBoardDataService>();
            if (boardData.TryGetCachedSnapshot(out var cached) && cached != null)
            {
                return cached;
            }
        }
        catch (Exception ex)
        {
            // Cached snapshot unavailable, try the full snapshot below.
            _logger.LogDebug(ex, "Cached board snapshot unavailable for offline statistics.");
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var boardData = scope.ServiceProvider.GetRequiredService<IBoardDataService>();
            return await boardData.GetSnapshotAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Snapshot failed, serve an empty board rather than failing the statistics page.
            _logger.LogWarning(ex, "Could not load the board snapshot for offline statistics.");
            return new BoardSnapshot([], [], []);
        }
    }

    private async Task<(DateOnly Today, TimeSpan? DayStart, IUserTimeZoneService? TimeZone)> GetTodayAndDayStartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var timeZone = scope.ServiceProvider.GetService<IUserTimeZoneService>();
            var prefsService = scope.ServiceProvider.GetService<IUserPreferencesService>();
            if (timeZone != null && prefsService != null)
            {
                var prefs = await prefsService.GetAsync(cancellationToken);
                var today = DailySchedule.LocalToday(timeZone, prefs.DayStartLocalTime);
                return (today, prefs.DayStartLocalTime, timeZone);
            }

            if (timeZone != null)
            {
                var today = DailySchedule.LocalToday(timeZone);
                return (today, null, timeZone);
            }
        }
        catch (Exception ex)
        {
            // Fall back to the UTC day when preferences or the timezone are unavailable.
            _logger.LogDebug(ex, "Could not load user preferences for offline statistics day boundaries.");
        }

        return (DateOnly.FromDateTime(DateTime.UtcNow), null, null);
    }

    private static IReadOnlyList<UserActivityEventRecord> FilterEvents(IReadOnlyList<UserActivityEventRecord> all, DateTimeOffset fromUtc, DateTimeOffset toUtc, string? tag, BoardSnapshot snapshot)
    {
        var allowedIds = GetAllowedIdsForTag(tag, snapshot);
        return [.. all.Where(e => e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc && (allowedIds == null || (e.BoardItemId != null && allowedIds.Contains(e.BoardItemId.Value))))];
    }

    private static HashSet<Guid>? GetAllowedIdsForTag(string? tag, BoardSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var wanted = tag.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (wanted.Length == 0)
        {
            return null;
        }

        HashSet<string> wantedSet = new(wanted, StringComparer.OrdinalIgnoreCase);
        var matched = snapshot.Habits.Concat(snapshot.Dailies).Concat(snapshot.Todos)
            .Where(b => BoardTagUtil.ParseTags(b.Tags).Any(t => wantedSet.Contains(t)))
            .Select(b => b.Id)
            .ToHashSet();

        return matched;
    }

    private static IReadOnlyList<string> GetDistinctTags(BoardSnapshot snapshot)
    {
        HashSet<string> set = new(snapshot.Habits.Concat(snapshot.Dailies).Concat(snapshot.Todos).SelectMany(b => BoardTagUtil.ParseTags(b.Tags)), StringComparer.OrdinalIgnoreCase);
        return [.. set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
    }

    private static IReadOnlyList<DailyItemStatsDto> GetDailyItems(BoardSnapshot snapshot)
    {
        return [.. snapshot.Dailies.Select(b => new DailyItemStatsDto(
            b.Id,
            b.Title,
            b.DailyStartDate,
            b.CreatedAtUtc is { } c ? DateOnly.FromDateTime(c.DateTime) : DateOnly.FromDateTime(DateTime.UtcNow),
            b.DailyRepeat,
            b.DailyRepeatInterval < 1 ? 1 : Math.Min(999, b.DailyRepeatInterval),
            DailyWeekdays.Normalize(b.DailyWeekdays),
            b.DailyLastCompletedOn))];
    }

    private static IReadOnlyList<HabitItemStatsDto> GetHabitItems(BoardSnapshot snapshot)
    {
        return [.. snapshot.Habits.Select(b => new HabitItemStatsDto(b.Id, b.Title, b.CreatedAtUtc is { } c ? DateOnly.FromDateTime(c.DateTime) : DateOnly.FromDateTime(DateTime.UtcNow)))];
    }

    private static int ComputeBoardHash(BoardSnapshot snapshot)
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + snapshot.Habits.Count;
            hash = hash * 31 + snapshot.Dailies.Count;
            hash = hash * 31 + snapshot.Todos.Count;
            foreach (var b in snapshot.Habits.Concat(snapshot.Dailies).Concat(snapshot.Todos))
            {
                hash = hash * 31 + b.Id.GetHashCode();
                if (b.Tags != null)
                {
                    hash = hash * 31 + b.Tags.GetHashCode(StringComparison.Ordinal);
                }

                hash = hash * 31 + b.Title.GetHashCode(StringComparison.Ordinal);
            }

            return hash;
        }
    }

    public void InvalidateForTags(IEnumerable<string>? tags)
    {
        if (tags == null || !tags.Any())
        {
            _overviewCache.Clear();
            _overviewEventCount.Clear();
            _overviewBoardHash.Clear();
            return;
        }

        var tagSet = new HashSet<string>(tags.SelectMany(t => BoardTagUtil.ParseTags(t)), StringComparer.OrdinalIgnoreCase);
        foreach (var key in _overviewCache.Keys.ToList())
        {
            var parts = key.Split('|');
            var cachedTag = parts.Length > 1 ? parts[1] : null;
            if (string.IsNullOrEmpty(cachedTag))
            {
                _overviewCache.TryRemove(key, out _);
                _overviewEventCount.TryRemove(key, out _);
                _overviewBoardHash.TryRemove(key, out _);
            }
            else
            {
                var cachedTags = new HashSet<string>(BoardTagUtil.ParseTags(cachedTag), StringComparer.OrdinalIgnoreCase);
                if (cachedTags.Overlaps(tagSet))
                {
                    _overviewCache.TryRemove(key, out _);
                    _overviewEventCount.TryRemove(key, out _);
                    _overviewBoardHash.TryRemove(key, out _);
                }
            }
        }
    }
}
