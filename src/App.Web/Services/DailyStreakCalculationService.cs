using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class DailyStreakCalculationService(IUserTimeZoneService timeZone)
{
    public static void ApplyManualStreakToEntity(
        BoardItemEntity entity,
        DateOnly? start,
        DailyRepeatType repeat,
        int interval,
        int streak,
        DateOnly today,
        bool wasCompleteForToday)
    {
        if (streak <= 0)
        {
            entity.DailyLastCompletedOn = null;
            entity.IsCompleted = false;
            return;
        }

        var notAfterPrevDays = today.AddDays(-1);
        var days = DailyStreakBackfill.GetLastNScheduledCompletionDays(
            start, repeat, interval, streak, notAfterPrevDays);
        if (days.Count == 0)
        {
            if (wasCompleteForToday)
            {
                entity.DailyLastCompletedOn = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                entity.IsCompleted = true;
            }
            else
            {
                entity.DailyLastCompletedOn = null;
                entity.IsCompleted = false;
            }

            return;
        }

        var newest = days[0];
        if (wasCompleteForToday)
        {
            entity.DailyLastCompletedOn = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            entity.IsCompleted = true;
        }
        else
        {
            entity.DailyLastCompletedOn = newest.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            entity.IsCompleted = false;
        }
    }

    public async Task ReconcileDailyStreakBackfillAsync(
        ApplicationDbContext dbContext,
        Guid userId,
        Guid itemId,
        DailyBackfillArgs args,
        DateOnly notAfter,
        CancellationToken cancellationToken)
    {
        var newSet = new HashSet<DateOnly>(DailyStreakBackfill.GetLastNScheduledCompletionDays(
            args.DailyStart, args.Repeat, args.Interval, args.Streak, notAfter));

        // Only synthetic backfill markers, the fixed UTC hour, can be reconciled away. Real toggles
        // are never removed. Filtering by the marker hour keeps the loaded set bounded by the
        // backfill history instead of the full activity log for the item.
        var toRemove = await dbContext.UserActivityEvents
            .Where(e => e.UserId == userId
                        && e.BoardItemId == itemId
                        && e.EventType == ActivityEventType.DailyComplete
                        && e.OccurredAtUtc.Hour == DailyStreakBackfill.StreakBackfillHourUtc)
            .ToListAsync(cancellationToken);
        foreach (var e in toRemove)
        {
            if (!DailyStreakBackfill.IsStreakBackfillTimestamp(e.OccurredAtUtc))
            {
                continue;
            }

            var day = DateOnly.FromDateTime(e.OccurredAtUtc.UtcDateTime);
            if (newSet.Contains(day))
            {
                continue;
            }

            dbContext.UserActivityEvents.Remove(e);
        }

        if (newSet.Count > 0)
        {
            var minDay = newSet.Min();
            var maxDay = newSet.Max();
            var rangeStart = new DateTimeOffset(minDay.Year, minDay.Month, minDay.Day, 0, 0, 0, TimeSpan.Zero);
            var rangeEnd = new DateTimeOffset(maxDay.Year, maxDay.Month, maxDay.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);

            var existingDates = await dbContext.UserActivityEvents
                .Where(e => e.UserId == userId
                            && e.BoardItemId == itemId
                            && e.EventType == ActivityEventType.DailyComplete
                            && e.OccurredAtUtc >= rangeStart
                            && e.OccurredAtUtc < rangeEnd)
                .Select(e => e.OccurredAtUtc)
                .ToListAsync(cancellationToken);
            var existingSet = new HashSet<DateOnly>(
                existingDates.Select(d => DateOnly.FromDateTime(d.UtcDateTime)));

            foreach (var d in newSet)
            {
                if (existingSet.Contains(d))
                {
                    continue;
                }

                dbContext.UserActivityEvents.Add(new UserActivityEventEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    OccurredAtUtc = DailyStreakBackfill.StreakBackfillOccurredAt(d),
                    EventType = ActivityEventType.DailyComplete,
                    BoardItemId = itemId
                });
            }
        }
    }

    public async Task<IReadOnlyDictionary<Guid, int>> BuildDailyStreakMapAsync(
        Guid userId,
        List<BoardItemEntity> dailies,
        DateOnly today,
        TimeSpan? dayStartLocalTime,
        ApplicationDbContext forQueries,
        CancellationToken cancellationToken = default)
    {
        if (dailies.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var minHistoryStart = FindMinHistoryStart(dailies, today);
        if (minHistoryStart is null)
        {
            return new Dictionary<Guid, int>();
        }

        // One day of margin on each side keeps boundary events for any timezone offset.
        var historyStartUtc = new DateTimeOffset(
            minHistoryStart.Value.Year,
            minHistoryStart.Value.Month,
            minHistoryStart.Value.Day,
            0,
            0,
            0,
            TimeSpan.Zero).AddDays(-1);
        var endUtcExclusive = new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, TimeSpan.Zero)
            .AddDays(2);

        var ids = dailies.Select(x => x.Id).ToList();
        var eventRows = await forQueries.UserActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId
                        && e.BoardItemId != null
                        && ids.Contains(e.BoardItemId.Value)
                        && (e.EventType == ActivityEventType.DailyComplete
                            || e.EventType == ActivityEventType.DailyUncomplete)
                        && e.OccurredAtUtc >= historyStartUtc
                        && e.OccurredAtUtc < endUtcExclusive)
            .Select(e => new { e.BoardItemId, e.OccurredAtUtc, e.EventType })
            .ToListAsync(cancellationToken);

        // Include events added to the change tracker but not yet saved
        foreach (var tracked in forQueries.ChangeTracker.Entries<UserActivityEventEntity>()
                     .Where(e => e.State == EntityState.Added
                                 && e.Entity.BoardItemId is { } bid
                                 && ids.Contains(bid)
                                 && e.Entity.OccurredAtUtc >= historyStartUtc
                                 && e.Entity.OccurredAtUtc < endUtcExclusive)
                     .Select(e => new { e.Entity.BoardItemId, e.Entity.OccurredAtUtc, e.Entity.EventType }))
        {
            eventRows.Add(tracked);
        }

        var byItem = new Dictionary<Guid, List<(DateTimeOffset, ActivityEventType)>>();
        foreach (var e in eventRows)
        {
            if (e.BoardItemId is not { } bid)
            {
                continue;
            }

            if (!byItem.TryGetValue(bid, out var list))
            {
                list = [];
                byItem[bid] = list;
            }

            list.Add((e.OccurredAtUtc, e.EventType));
        }

        return ComputeDailyStreaks(dailies, today, timeZone, dayStartLocalTime, byItem);
    }

    public static DateOnly? FindMinHistoryStart(List<BoardItemEntity> dailies, DateOnly today)
    {
        DateOnly? minHistoryStart = null;
        foreach (var daily in dailies)
        {
            GetDailyEntitySchedule(daily, out var start, out var repeat, out var interval);
            var hintStreak = Math.Min(DailyStreakCalculator.MaxStreak, daily.Counter + 50);
            var historyStart = DailySchedule.StreakHistoryScheduleStart(
                start,
                today,
                repeat,
                interval,
                hintStreak);
            if (minHistoryStart == null || historyStart < minHistoryStart)
            {
                minHistoryStart = historyStart;
            }
        }
        return minHistoryStart;
    }

    public static Dictionary<Guid, int> ComputeDailyStreaks(
        List<BoardItemEntity> dailies,
        DateOnly today,
        IUserTimeZoneService timeZone,
        TimeSpan? dayStartLocalTime,
        Dictionary<Guid, List<(DateTimeOffset, ActivityEventType)>> byItem)
    {
        var outMap = new Dictionary<Guid, int>(dailies.Count);
        foreach (var ent in dailies)
        {
            byItem.TryGetValue(ent.Id, out var evList);
            var grouped = DailyStreakCalculator.GroupDailyEventsByLocalDay(evList ?? [], timeZone, dayStartLocalTime);
            var lastC = ent.DailyLastCompletedOn is { } l ? DateOnly.FromDateTime(l) : (DateOnly?)null;
            GetDailyEntitySchedule(ent, out var start, out var repeat, out var interval);
            outMap[ent.Id] = DailyStreakCalculator.ComputeStreak(
                start,
                repeat,
                interval,
                today,
                grouped,
                lastC);
        }
        return outMap;
    }

    public static void GetDailyEntitySchedule(
        BoardItemEntity entity,
        out DateOnly? start,
        out DailyRepeatType repeat,
        out int interval)
    {
        start = entity.DailyStartDate is { } d0 ? DateOnly.FromDateTime(d0) : null;
        (repeat, interval) = ResolveSchedule(entity);
    }

    public static (DailyRepeatType repeat, int interval) ResolveSchedule(BoardItemEntity entity)
    {
        if (entity.Section != BoardSection.Daily)
        {
            return (DailyRepeatType.Daily, 1);
        }

        var repeat = Enum.IsDefined((DailyRepeatType)entity.DailyRepeatType)
            ? (DailyRepeatType)entity.DailyRepeatType
            : DailyRepeatType.Daily;
        var interval = entity.DailyRepeatInterval < 1 ? 1 : Math.Min(999, entity.DailyRepeatInterval);
        return (repeat, interval);
    }
}
