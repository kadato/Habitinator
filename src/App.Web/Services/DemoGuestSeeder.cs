using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Bogus;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

/// <summary>Seeds the demo guest board and synthetic activity history for statistics.</summary>
public static class DemoGuestSeeder
{
    private const int DemoRandomSeed = 2026_04_24;
    private const int HeatmapDataDays = 370;
    /// <summary>Seeded heatmap is ~1.8k events.</summary>
    private const int HeatmapPresentThreshold = 1_500;

    /// <summary>Board insert adds only a few streak backfill rows before the heatmap is generated.</summary>
    private const int BoardBackfillEventMax = 10;

    /// <summary>Inserts demo board items when missing, then fills activity when the heatmap is sparse.</summary>
    public static async Task SeedIfMissingAsync(
        ApplicationDbContext db,
        IBoardChangeNotifier notifier,
        Guid guestUserId,
        CancellationToken cancellationToken = default)
    {
        await SeedBoardIfMissingAsync(db, notifier, guestUserId, cancellationToken);
        await SeedDemoActivityIfMissingAsync(db, guestUserId, cancellationToken);
    }

    /// <summary>Replaces the full demo board and activity log (caller must clear guest data first).</summary>
    public static async Task ReseedAllAsync(
        ApplicationDbContext db,
        IBoardChangeNotifier notifier,
        Guid guestUserId,
        CancellationToken cancellationToken = default)
    {
        await InsertDemoBoardAsync(db, notifier, guestUserId, cancellationToken);
        await ReseedActivityAsync(db, guestUserId, cancellationToken);
    }

    public static async Task SeedBoardIfMissingAsync(
        ApplicationDbContext db,
        IBoardChangeNotifier notifier,
        Guid guestUserId,
        CancellationToken cancellationToken = default)
    {
        if (await BoardSeedHelpers.HasLiveBoardItemsAsync(db, guestUserId, cancellationToken))
        {
            return;
        }

        await InsertDemoBoardAsync(db, notifier, guestUserId, cancellationToken);
    }

    /// <summary>Inserts the full demo board (habits, dailies, to-dos with tags, checklists, due dates).
    ///     Caller must ensure this user's board rows are cleared when replacing existing data.</summary>
    public static async Task InsertDemoBoardAsync(
        ApplicationDbContext db,
        IBoardChangeNotifier notifier,
        Guid guestUserId,
        CancellationToken cancellationToken = default)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var today = DailySchedule.LocalToday();
        var dayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        static DateTime UtcDay(DateOnly d) => new(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);

        var dailyWorkoutId = Guid.NewGuid();
        var dailyDeepId = Guid.NewGuid();

        var workoutCheck = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "5 min warm-up", false),
            new DailyChecklistItem(Guid.NewGuid(), "20 min main block", false),
            new DailyChecklistItem(Guid.NewGuid(), "Track progress in the journal", false)
        ]);
        var deepCheck = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Close email / chat", true),
            new DailyChecklistItem(Guid.NewGuid(), "Single focus task (45+ min)", false),
            new DailyChecklistItem(Guid.NewGuid(), "Short review of what got done", false)
        ]);
        var plantsCheck = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Kitchen plants", false),
            new DailyChecklistItem(Guid.NewGuid(), "Windowsill succulents", true),
            new DailyChecklistItem(Guid.NewGuid(), "Patio (if in season)", false)
        ]);
        var groceriesSub = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Vegetables", true),
            new DailyChecklistItem(Guid.NewGuid(), "Dairy", false),
            new DailyChecklistItem(Guid.NewGuid(), "Snacks for the week", false)
        ]);
        var tripSub = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Book transport", true),
            new DailyChecklistItem(Guid.NewGuid(), "Packing list", false),
            new DailyChecklistItem(Guid.NewGuid(), "Check weather", false)
        ]);
        var skincareSub = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "AM routine", true),
            new DailyChecklistItem(Guid.NewGuid(), "PM routine", false)
        ]);

        var order = 0;
        void AddBoardRow(BoardItemEntity row) => BoardSeedHelpers.AddBoardRow(db, row, ref order, utcNow);

        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = guestUserId,
            Section = BoardSection.Habit,
            Title = "Drink a glass of water",
            Notes = "Log each glass with + so statistics show up on the board.",
            Tags = "health, hydration",
            Counter = 4,
            NegativeCounter = 1
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = guestUserId,
            Section = BoardSection.Habit,
            Title = "Read 10 minutes",
            Tags = "focus, learning",
            Counter = 2,
            NegativeCounter = 0
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = guestUserId,
            Section = BoardSection.Habit,
            Title = "Plan the week",
            Notes = "Habit uses a weekly counter reset in the edit dialog to match a Sunday review.",
            Tags = "planning, work",
            ResetPeriod = (int)HabitResetPeriod.Weekly,
            Counter = 0,
            NegativeCounter = 0
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = dailyWorkoutId,
            UserId = guestUserId,
            Section = BoardSection.Daily,
            Title = "Workout",
            Notes = "Completing a daily grows your streak - see the flame in the card footer. Miss a day and it resets.",
            Tags = "health, body",
            ChecklistJson = workoutCheck,
            IsCompleted = false,
            DailyStartDate = dayStart,
            DailyRepeatType = (int)DailyRepeatType.Daily,
            DailyRepeatInterval = 1,
            DailyLastCompletedOn = null
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = dailyDeepId,
            UserId = guestUserId,
            Section = BoardSection.Daily,
            Title = "Deep work block",
            Notes = "Try the focus timer in the toolbar - finished sessions count toward your statistics.",
            Tags = "focus, work",
            ChecklistJson = deepCheck,
            IsCompleted = true,
            DailyStartDate = dayStart,
            DailyRepeatType = (int)DailyRepeatType.Daily,
            DailyRepeatInterval = 1,
            DailyLastCompletedOn = dayStart
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = guestUserId,
            Section = BoardSection.Daily,
            Title = "Water the plants",
            Notes = "Weekly schedule - due every week from its start date. Change it in the edit dialog.",
            Tags = "home, health",
            ChecklistJson = plantsCheck,
            IsCompleted = false,
            DailyStartDate = dayStart,
            DailyRepeatType = (int)DailyRepeatType.Weekly,
            DailyRepeatInterval = 1,
            DailyLastCompletedOn = null
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = guestUserId,
            Section = BoardSection.Daily,
            Title = "Inbox review",
            Notes = "Monthly schedule - due on the same calendar day each month when it lands on a scheduled date.",
            Tags = "work",
            IsCompleted = false,
            DailyStartDate = firstOfMonth,
            DailyRepeatType = (int)DailyRepeatType.Monthly,
            DailyRepeatInterval = 1,
            DailyLastCompletedOn = null
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = guestUserId,
            Section = BoardSection.Todo,
            Title = "Buy groceries",
            Notes = "Open the card to edit the subtasks or set a due date.",
            Tags = "home, errands",
            ChecklistJson = groceriesSub,
            IsCompleted = false,
            DailyStartDate = dayStart
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = guestUserId,
            Section = BoardSection.Todo,
            Title = "Submit project draft",
            Notes = "Due in a few days. Open the card to set another due date or subtasks.",
            Tags = "school, focus",
            IsCompleted = false,
            DailyStartDate = UtcDay(today.AddDays(3))
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = guestUserId,
            Section = BoardSection.Todo,
            Title = "Skincare routine (evening)",
            Notes = "Subtasks stay on the card - tick each one off as you go.",
            Tags = "health, personal",
            ChecklistJson = skincareSub,
            IsCompleted = false,
            DailyStartDate = null
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = guestUserId,
            Section = BoardSection.Todo,
            Title = "Return library books",
            Notes = "Overdue? The due date badge turns red. Open the card to move it.",
            Tags = "school, errands",
            IsCompleted = false,
            DailyStartDate = UtcDay(today.AddDays(-2))
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = guestUserId,
            Section = BoardSection.Todo,
            Title = "Weekend trip",
            Tags = "personal, travel",
            ChecklistJson = tripSub,
            IsCompleted = false,
            DailyStartDate = UtcDay(today.AddDays(5))
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = guestUserId,
            Section = BoardSection.Todo,
            Title = "File expense report",
            Notes = "Completed to-dos stay on the board - archive this one from the options menu when done.",
            Tags = "work",
            IsCompleted = true,
            DailyStartDate = UtcDay(today.AddDays(-1))
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = guestUserId,
            Section = BoardSection.Todo,
            Title = "Weekly review",
            Notes = "Recurring to-do - completing it moves the due date forward by a week.",
            Tags = "work, planning",
            IsCompleted = false,
            DailyStartDate = UtcDay(today.AddDays(1)),
            TodoRepeatIntervalDays = 7
        });

        for (var i = 0; i < 3; i++)
        {
            var d = today.AddDays(-(2 - i));
            db.UserActivityEvents.Add(new UserActivityEventEntity
            {
                Id = Guid.NewGuid(),
                UserId = guestUserId,
                OccurredAtUtc = DailyStreakCalculator.BackdatedDailyEventOccurredAt(d),
                EventType = ActivityEventType.DailyComplete,
                BoardItemId = dailyDeepId,
                CustomLabel = "Deep work block"
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotifyBoardChangedAsync(guestUserId, cancellationToken);
    }

    /// <summary>Removes all guest activity events and inserts a fresh year-long demo series.</summary>
    public static async Task ReseedActivityAsync(
        ApplicationDbContext db,
        Guid guestUserId,
        CancellationToken cancellationToken = default)
    {
        await RemoveAllActivityEventsAsync(db, guestUserId, cancellationToken);

        await SeedDemoActivityCoreAsync(db, guestUserId, cancellationToken);
    }

    internal static async Task RemoveAllActivityEventsAsync(
        ApplicationDbContext db,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.UserActivityEvents.Where(e => e.UserId == userId).ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            db.UserActivityEvents.RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedDemoActivityIfMissingAsync(
        ApplicationDbContext db,
        Guid guestUserId,
        CancellationToken cancellationToken)
    {
        // Only fill an empty (or board-only backfill) log. Never append a second year on startup.
        // Full replace is ReseedActivityAsync (ForceReseed / ForceReseedActivity only).
        var n = await db.UserActivityEvents.CountAsync(e => e.UserId == guestUserId, cancellationToken);
        if (n >= HeatmapPresentThreshold)
        {
            return;
        }

        if (n > BoardBackfillEventMax)
        {
            return;
        }

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

        if (boardItems.Count == 0)
        {
            return;
        }

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
        var pool = eventType switch
        {
            ActivityEventType.HabitPlus or ActivityEventType.HabitMinus => habitIds,
            ActivityEventType.DailyComplete or ActivityEventType.DailyUncomplete => dailyIds,
            ActivityEventType.TodoComplete or ActivityEventType.TodoUncomplete => todoIds,
            _ => anyBoardItemIds
        };

        if (pool.Count == 0)
        {
            pool = anyBoardItemIds;
        }

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
