using App.Shared.RCL.Models;
using App.Web.Data;

using Bogus;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public static class GuestActivityDemoSeeder
{
    private const int DemoRandomSeed = 2026_04_24;
    private const int HeatmapDataDays = 370;

    public static async Task SeedIfMissingAsync(ApplicationDbContext db, Guid guestUserId,
        CancellationToken cancellationToken = default)
    {
        // Board demo seed adds a few activity rows (e.g. daily streak backfill). A full guest heatmap is
        // thousands of events — if we have that many, skip. Otherwise fill in the year-long demo series.
        const int heatmapPresentThreshold = 2_000;
        var n = await db.UserActivityEvents.CountAsync(e => e.UserId == guestUserId, cancellationToken);
        if (n > heatmapPresentThreshold) return;

        var boardItemIds = await db.BoardItems
            .AsNoTracking()
            .Where(x => x.UserId == guestUserId && x.DeletedAtUtc == null)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var faker = new Faker
        {
            Random = new Randomizer(DemoRandomSeed)
        };

        var end = DailySchedule.UtcToday;
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
                Guid? boardItemId = boardItemIds.Count > 0 && faker.Random.Bool(0.45f)
                    ? faker.PickRandom(boardItemIds)
                    : null;

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
                    DurationSeconds = durationSec
                });
            }
        }

        db.UserActivityEvents.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);
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
