using App.Shared.RCL.Models;
using App.Web.Data;
using Bogus;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public static class GuestActivityDemoSeeder
{
    private const int DemoRandomSeed = 2026_04_24;
    private const int HeatmapDataDays = 370;

    public static async Task SeedIfMissingAsync(ApplicationDbContext db, Guid guestUserId, CancellationToken cancellationToken = default)
    {
        if (await db.UserActivityEvents.AnyAsync(e => e.UserId == guestUserId, cancellationToken))
        {
            return;
        }

        List<Guid> boardItemIds = await db.BoardItems
            .AsNoTracking()
            .Where(x => x.UserId == guestUserId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var faker = new Faker
        {
            Random = new Randomizer(DemoRandomSeed)
        };

        DateOnly end = DailySchedule.UtcToday;
        DateOnly start = end.AddDays(-(HeatmapDataDays - 1));

        var events = new List<UserActivityEventEntity>();
        for (DateOnly day = start; day <= end; day = day.AddDays(1))
        {
            bool weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            int maxPerDay = weekend ? 5 : 12;
            int count = faker.Random.Int(0, maxPerDay);
            for (int i = 0; i < count; i++)
            {
                int minuteOfDay = faker.Random.Int(0, 1439);
                var time = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minuteOfDay));
                DateTime utc = day.ToDateTime(time, DateTimeKind.Utc);
                var occurred = new DateTimeOffset(utc, TimeSpan.Zero);

                ActivityEventType eventType = PickEventType(faker);
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
