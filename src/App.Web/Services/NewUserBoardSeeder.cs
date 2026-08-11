using App.Shared.RCL.Models;
using App.Web.Data;

namespace App.Web.Services;

/// <summary>
/// Seeds a small "getting started" board for brand-new accounts so the first login shows one
/// item per section that explains what it is and how to use it.
/// </summary>
public static class NewUserBoardSeeder
{
    /// <summary>Tag applied to starter items so users can recognize and delete them.</summary>
    public const string GettingStartedTag = "getting-started";

    /// <summary>Inserts the starter board only when the user has no live board items.</summary>
    public static async Task SeedIfMissingAsync(
        ApplicationDbContext db,
        IBoardChangeNotifier notifier,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (await BoardSeedHelpers.HasLiveBoardItemsAsync(db, userId, cancellationToken))
        {
            return;
        }

        await InsertStarterBoardAsync(db, notifier, userId, cancellationToken);
    }

    private static async Task InsertStarterBoardAsync(
        ApplicationDbContext db,
        IBoardChangeNotifier notifier,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var today = DailySchedule.LocalToday();
        var dayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var checklist = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Subtasks live inside the card", true),
            new DailyChecklistItem(Guid.NewGuid(), "Edit the card to add your own", false)
        ]);

        var order = 0;
        void AddBoardRow(BoardItemEntity row) => BoardSeedHelpers.AddBoardRow(db, row, ref order, utcNow);

        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Section = BoardSection.Habit,
            Title = "How habits work",
            Notes = "A habit counts how often you do something. Press + every time you do it, or - to log a slip. " +
                    "The counter resets daily, weekly, or monthly - open this card to choose and to add tags.",
            Tags = GettingStartedTag,
            TrackPlus = true,
            TrackMinus = true
        });

        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Section = BoardSection.Daily,
            Title = "How dailies work",
            Notes = "A daily repeats on a schedule: daily, weekly, monthly, or yearly. Tick the checkbox to complete " +
                    "today's cycle and grow your streak. Open this card to change the schedule or subtasks.",
            Tags = GettingStartedTag,
            ChecklistJson = checklist,
            IsCompleted = false,
            DailyStartDate = dayStart,
            DailyRepeatType = (int)DailyRepeatType.Daily,
            DailyRepeatInterval = 1,
            DailyLastCompletedOn = null
        });

        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Section = BoardSection.Todo,
            Title = "How to-dos work",
            Notes = "A to-do is a one-off task. Set a due date, add subtasks, or make it recurring so it re-dates " +
                    "itself when done. Tick the checkbox to finish it, or archive it from the options menu.",
            Tags = GettingStartedTag,
            IsCompleted = false,
            DailyStartDate = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        });

        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotifyBoardChangedAsync(userId, cancellationToken);
    }
}
