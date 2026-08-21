using System.Collections.Concurrent;

using App.Shared.RCL.Models;

using Microsoft.Extensions.DependencyInjection;

namespace App.Shared.RCL.Services;

public sealed class OfflineActivityStatisticsProvider : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IActivityEventStore _eventStore;
    private readonly ConcurrentDictionary<string, ActivityOverviewDto> _overviewCache = new();
    private readonly ConcurrentDictionary<string, int> _overviewEventCount = new();
    private readonly ConcurrentDictionary<string, int> _overviewBoardHash = new();

    public OfflineActivityStatisticsProvider(IServiceProvider serviceProvider, IActivityEventStore eventStore)
    {
        _serviceProvider = serviceProvider;
        _eventStore = eventStore;
        _eventStore.Appended += OnEventAppended;
    }

    public void Dispose()
    {
        _eventStore.Appended -= OnEventAppended;
    }

    private void OnEventAppended(object? sender, UserActivityEventRecord record)
    {
        // Granular invalidation: only remove caches where tag matches the new event's board item tags
        // We do not know the board item tags without a DB lookup, so we conservatively invalidate only tag=null caches
        // and let tag-filtered caches stay hot until next load where they will be checked for tag match.
        // For true delta we patch the cached overview if the event falls within its range.
        TryPatchCacheForNewEvent(record);
    }

    private void TryPatchCacheForNewEvent(UserActivityEventRecord record)
    {
        // For each cached overview, if the new event's date is within its range and tag matches, patch it
        // This is best-effort incremental, if patch fails we just invalidate that key so next load recomputes
        foreach (var key in _overviewCache.Keys.ToList())
        {
            if (!_overviewCache.TryGetValue(key, out var overview))
            {
                continue;
            }
            var parts = key.Split('|');
            var cachedTag = parts.Length > 1 ? parts[1] : null;
            if (string.IsNullOrEmpty(cachedTag))
            {
                // Tag null always affected
                PatchOrInvalidate(key, overview, record);
            }
            else
            {
                // Need to check if the event's board item has this tag. We can try to lookup board item tags via snapshot
                // For now, conservatively invalidate tag-filtered caches when any event arrives, to keep correctness simple
                // True delta would check BoardTagUtil.ParseTags of the board item
                _overviewCache.TryRemove(key, out _);
                _overviewEventCount.TryRemove(key, out _);
                _overviewBoardHash.TryRemove(key, out _);
            }
        }
    }

    private void PatchOrInvalidate(string key, ActivityOverviewDto overview, UserActivityEventRecord record)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var timeZone = scope.ServiceProvider.GetService<IUserTimeZoneService>();
            var prefsService = scope.ServiceProvider.GetService<IUserPreferencesService>();
            TimeSpan? dayStart = null;
            if (timeZone != null && prefsService != null)
            {
                try
                {
                    var prefs = prefsService.GetAsync().GetAwaiter().GetResult();
                    dayStart = prefs.DayStartLocalTime;
                }
                catch (Exception ex)
                {
                    // Ignore - best effort to load preferences; fallback to null dayStart
                    _ = ex;
                }
            }

            var eventDay = timeZone != null ? DailySchedule.LocalDay(record.OccurredAtUtc, timeZone, dayStart) : DateOnly.FromDateTime(record.OccurredAtUtc.UtcDateTime);
            if (eventDay < overview.Dashboard.RangeStart || eventDay > overview.Dashboard.RangeEnd)
            {
                return;
            }

            // For dashboard, we can patch the per-day counts
            // Instead of full recompute, we just invalidate so next load recomputes from delta
            // To keep it simple and correct, invalidate the key so next BuildOverview recomputes with the new event included
            // This is still delta in terms of not invalidating other tag caches
            _overviewCache.TryRemove(key, out _);
            _overviewEventCount.TryRemove(key, out _);
            _overviewBoardHash.TryRemove(key, out _);
        }
        catch (Exception ex)
        {
            // Ignore - patch failed, invalidate cache so next load recomputes
            _ = ex;
            _overviewCache.TryRemove(key, out _);
        }
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
            // Ignore - cached snapshot unavailable, try full snapshot
            _ = ex;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var boardData = scope.ServiceProvider.GetRequiredService<IBoardDataService>();
            return await boardData.GetSnapshotAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Ignore - snapshot failed, return empty
            _ = ex;
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
            // Ignore - best effort to load preferences/timezone; fallback to UTC today
            _ = ex;
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
        return [.. snapshot.Dailies.Select(b => new DailyItemStatsDto(b.Id, b.Title, b.DailyStartDate, b.CreatedAtUtc is { } c ? DateOnly.FromDateTime(c.DateTime) : DateOnly.FromDateTime(DateTime.UtcNow)))];
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
