using App.Shared.RCL.Models;
using App.Web.Data;

using Bogus;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

/// <summary>Seeds the demo guest board and synthetic activity history for statistics.</summary>
public static class DemoGuestSeeder
{
    private const int DemoRandomSeed = 2026_04_24;
    private const int HeatmapDataDays = 370;
    private const int HeatmapPresentThreshold = 2_000;

    /// <summary>Inserts demo board items when missing, then fills activity when the heatmap is sparse.</summary>
    public static async Task SeedIfMissingAsync(
        ApplicationDbContext db,
        BoardPersistenceService board,
        Guid guestUserId,
        CancellationToken cancellationToken = default)
    {
        await board.SeedBoardDataIfMissingAsync(guestUserId, cancellationToken);
        await SeedDemoActivityIfMissingAsync(db, guestUserId, cancellationToken);
    }

    /// <summary>Replaces the full demo board and activity log (caller must clear guest data first).</summary>
    public static async Task ReseedAllAsync(
        ApplicationDbContext db,
        BoardPersistenceService board,
        Guid guestUserId,
        CancellationToken cancellationToken = default)
    {
        await board.InsertDemoBoardDataAsync(guestUserId, cancellationToken);
        await ReseedActivityAsync(db, guestUserId, cancellationToken);
    }

    /// <summary>Removes all guest activity events and inserts a fresh year-long demo series.</summary>
    public static async Task ReseedActivityAsync(
        ApplicationDbContext db,
        Guid guestUserId,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.UserActivityEvents.Where(e => e.UserId == guestUserId).ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            db.UserActivityEvents.RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        await SeedDemoActivityCoreAsync(db, guestUserId, cancellationToken);
    }

    private static async Task SeedDemoActivityIfMissingAsync(
        ApplicationDbContext db,
        Guid guestUserId,
        CancellationToken cancellationToken)
    {
        // Board demo seed adds a few activity rows (e.g. daily streak backfill). A full guest heatmap is
        // thousands of events — if we have that many, skip. Otherwise fill in the year-long demo series.
        var n = await db.UserActivityEvents.CountAsync(e => e.UserId == guestUserId, cancellationToken);
        if (n > HeatmapPresentThreshold) return;

        await SeedDemoActivityCoreAsync(db, guestUserId, cancellationToken);
    }

    private static async Task SeedDemoActivityCoreAsync(
        ApplicationDbContext db,
        Guid guestUserId,
        CancellationToken cancellationToken)
    {
        var boardItems = await db.BoardItems
            .AsNoTracking()
            .Where(x => x.UserId == guestUserId && x.DeletedAtUtc == null)
            .Select(x => new { x.Id, x.Section, x.Title })
            .ToListAsync(cancellationToken);

        if (boardItems.Count == 0) return;

        var titlesById = boardItems.ToDictionary(x => x.Id, x => x.Title);
        var habitIds = boardItems.Where(x => x.Section == BoardSection.Habit).Select(x => x.Id).ToList();
        var dailyIds = boardItems.Where(x => x.Section == BoardSection.Daily).Select(x => x.Id).ToList();
        var todoIds = boardItems.Where(x => x.Section == BoardSection.Todo).Select(x => x.Id).ToList();
        var anyBoardItemIds = boardItems.Select(x => x.Id).ToList();

        var faker = new Faker
        {
            Random = new Randomizer(DemoRandomSeed)
        };

        var end = DailySchedule.LocalToday();
        var start = end.AddDays(-(HeatmapDataDays - 1));

        var events = new List<UserActivityEventEntity>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var maxPerDay = weekend ? 5 : 12;
            var count = faker.Random.Int(0, maxPerDay);
            for (var i = 0; i < count; i++)
            {
                var minuteOfDay = faker.Random.Int(0, 1439);
                var time = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minuteOfDay));
                var utc = day.ToDateTime(time, DateTimeKind.Utc);
                var occurred = new DateTimeOffset(utc, TimeSpan.Zero);

                var eventType = PickEventType(faker);
                var boardItemId = PickBoardItemId(eventType, habitIds, dailyIds, todoIds, anyBoardItemIds, faker);
                var title = titlesById[boardItemId];

                int? durationSec = eventType == ActivityEventType.TimerSession
                    ? faker.Random.Int(120, 5400)
                    : null;

                events.Add(new UserActivityEventEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = guestUserId,
                    OccurredAtUtc = occurred,
                    EventType = eventType,
                    BoardItemId = boardItemId,
                    DurationSeconds = durationSec,
                    CustomLabel = title
                });
            }
        }

        db.UserActivityEvents.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Guid PickBoardItemId(
        ActivityEventType eventType,
        IReadOnlyList<Guid> habitIds,
        IReadOnlyList<Guid> dailyIds,
        IReadOnlyList<Guid> todoIds,
        IReadOnlyList<Guid> anyBoardItemIds,
        Faker faker)
    {
        IReadOnlyList<Guid> pool = eventType switch
        {
            ActivityEventType.HabitPlus or ActivityEventType.HabitMinus => habitIds,
            ActivityEventType.DailyComplete or ActivityEventType.DailyUncomplete => dailyIds,
            ActivityEventType.TodoComplete or ActivityEventType.TodoUncomplete => todoIds,
            _ => anyBoardItemIds
        };

        if (pool.Count == 0)
            pool = anyBoardItemIds;

        return pool[faker.Random.Int(0, pool.Count - 1)];
    }

    private static ActivityEventType PickEventType(Faker faker)
    {
        return faker.Random.ArrayElement(
        [
            ActivityEventType.HabitPlus,
            ActivityEventType.HabitPlus,
            ActivityEventType.HabitMinus,
            ActivityEventType.DailyComplete,
            ActivityEventType.DailyComplete,
            ActivityEventType.TodoComplete,
            ActivityEventType.TimerSession,
            ActivityEventType.TimerSession,
            ActivityEventType.TimerSession,
            ActivityEventType.DailyUncomplete,
            ActivityEventType.TodoUncomplete
        ]);
    }
}
