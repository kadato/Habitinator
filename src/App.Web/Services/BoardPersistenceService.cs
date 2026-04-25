using App.Shared.RCL.Models;
using App.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class BoardPersistenceService
{
    private readonly ApplicationDbContext _dbContext;

    public BoardPersistenceService(ApplicationDbContext dbContext) =>
        _dbContext = dbContext;

    public async Task<BoardSnapshot> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        List<BoardItemEntity> items = await _dbContext.BoardItems
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        DateOnly today = DailySchedule.UtcToday;
        return new BoardSnapshot(
            items.Where(x => x.Section == BoardSection.Habit)
                .OrderBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(ToModel)
                .ToList(),
            items.Where(x => x.Section == BoardSection.Daily)
                .OrderBy(x => IsDailyEntityCompleteForToday(x, today) ? 1 : 0)
                .ThenBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(ToModel)
                .ToList(),
            items.Where(x => x.Section == BoardSection.Todo)
                .OrderBy(x => x.IsCompleted)
                .ThenBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(ToModel)
                .ToList());
    }

    public async Task<BoardItem> CreateItemAsync(Guid userId, BoardSection section, string title, CancellationToken cancellationToken = default)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var entity = new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Section = section,
            Title = title,
            Notes = null,
            Tags = null,
            TrackPlus = true,
            TrackMinus = true,
            ResetPeriod = (int)HabitResetPeriod.Daily,
            IsCompleted = false,
            Counter = 0,
            NegativeCounter = 0,
            DailyStartDate = section == BoardSection.Daily
                ? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 0, 0, 0, DateTimeKind.Utc)
                : null,
            DailyRepeatType = (int)DailyRepeatType.Daily,
            DailyRepeatInterval = 1,
            ChecklistJson = null,
            DailyLastCompletedOn = null,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };

        _dbContext.BoardItems.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<BoardItem?> RenameItemAsync(Guid userId, BoardSection section, Guid itemId, string title, CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == section && x.Id == itemId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Title = title;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<bool> DeleteItemAsync(Guid userId, BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == section && x.Id == itemId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        _dbContext.BoardItems.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<BoardItem?> ToggleItemAsync(Guid userId, BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == section && x.Id == itemId, cancellationToken);
        if (entity is null || section == BoardSection.Habit)
        {
            return null;
        }

        if (section == BoardSection.Daily)
        {
            DateOnly today = DailySchedule.UtcToday;
            bool wasCompleteForToday = IsDailyEntityCompleteForToday(entity, today);
            if (wasCompleteForToday)
            {
                entity.DailyLastCompletedOn = null;
                entity.IsCompleted = false;
            }
            else
            {
                entity.DailyLastCompletedOn = new DateTime(today.Year, today.Month, today.Day, 0, 0, 0, DateTimeKind.Utc);
                entity.IsCompleted = true;
            }

            AddActivityEvent(userId, wasCompleteForToday ? ActivityEventType.DailyUncomplete : ActivityEventType.DailyComplete, itemId);
        }
        else
        {
            bool wasCompleted = entity.IsCompleted;
            entity.IsCompleted = !entity.IsCompleted;
            AddActivityEvent(userId, wasCompleted ? ActivityEventType.TodoUncomplete : ActivityEventType.TodoComplete, itemId);
        }

        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task LogTimerSessionAsync(
        Guid userId,
        TimeSpan duration,
        Guid? boardItemId,
        CancellationToken cancellationToken = default)
    {
        int sec = (int)Math.Min((double)int.MaxValue, Math.Max(0, duration.TotalSeconds));
        if (sec == 0)
        {
            return;
        }

        AddActivityEvent(userId, ActivityEventType.TimerSession, boardItemId, sec);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<BoardItem?> IncrementHabitPlusAsync(Guid userId, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId, cancellationToken);
        if (entity is null || !entity.TrackPlus)
        {
            return null;
        }

        entity.Counter++;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddActivityEvent(userId, ActivityEventType.HabitPlus, itemId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<BoardItem?> IncrementHabitMinusAsync(Guid userId, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId, cancellationToken);
        if (entity is null || !entity.TrackMinus)
        {
            return null;
        }

        entity.NegativeCounter++;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddActivityEvent(userId, ActivityEventType.HabitMinus, itemId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<BoardItem?> UpdateHabitAsync(
        Guid userId,
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        bool trackPlus,
        bool trackMinus,
        HabitResetPeriod resetPeriod,
        int counter,
        int negativeCounter,
        string? checklistJson = null,
        CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (!trackPlus && !trackMinus)
        {
            trackPlus = true;
            trackMinus = true;
        }

        entity.Title = title;
        entity.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        entity.Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim();
        entity.TrackPlus = trackPlus;
        entity.TrackMinus = trackMinus;
        entity.ResetPeriod = (int)resetPeriod;
        entity.Counter = Math.Max(0, counter);
        entity.NegativeCounter = Math.Max(0, negativeCounter);
        entity.ChecklistJson = string.IsNullOrWhiteSpace(checklistJson) ? null : checklistJson.Trim();
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<BoardItem?> UpdateTodoAsync(
        Guid userId,
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        string? checklistJson,
        DateTime? dueDate,
        CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == BoardSection.Todo && x.Id == itemId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        DateTime? dueUtc = dueDate is { } d
            ? new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc)
            : null;

        entity.Title = title;
        entity.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        entity.Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim();
        entity.ChecklistJson = string.IsNullOrWhiteSpace(checklistJson) ? null : checklistJson.Trim();
        entity.DailyStartDate = dueUtc;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<BoardItem?> UpdateDailyAsync(
        Guid userId,
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        DateTime? startDate,
        DailyRepeatType repeatType,
        int repeatInterval,
        string? checklistJson,
        int streak,
        CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == BoardSection.Daily && x.Id == itemId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        int n = Math.Max(1, Math.Min(999, repeatInterval));
        DateTime? startUtc = startDate is { } s
            ? new DateTime(s.Year, s.Month, s.Day, 0, 0, 0, DateTimeKind.Utc)
            : null;
        int streakClamped = Math.Max(0, Math.Min(9999, streak));

        entity.Title = title;
        entity.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        entity.Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim();
        entity.DailyStartDate = startUtc;
        entity.DailyRepeatType = (int)repeatType;
        entity.DailyRepeatInterval = n;
        entity.ChecklistJson = string.IsNullOrWhiteSpace(checklistJson) ? null : checklistJson.Trim();
        entity.Counter = streakClamped;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task SeedBoardDataIfMissingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        bool hasItems = await _dbContext.BoardItems.AnyAsync(x => x.UserId == userId, cancellationToken);
        if (hasItems)
        {
            return;
        }

        var utcNow = DateTimeOffset.UtcNow;
        _dbContext.BoardItems.AddRange(
            new BoardItemEntity { Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Habit, Title = "Drink a glass of water", Counter = 3, NegativeCounter = 2, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow },
            new BoardItemEntity { Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Habit, Title = "Read 10 pages", Counter = 1, NegativeCounter = 0, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow },
            new BoardItemEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Section = BoardSection.Daily,
                Title = "Workout",
                IsCompleted = false,
                DailyStartDate = new DateTime(utcNow.UtcDateTime.Year, utcNow.UtcDateTime.Month, utcNow.UtcDateTime.Day, 0, 0, 0, DateTimeKind.Utc),
                DailyRepeatType = (int)DailyRepeatType.Daily,
                DailyRepeatInterval = 1,
                DailyLastCompletedOn = null,
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow
            },
            new BoardItemEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Section = BoardSection.Daily,
                Title = "Deep work block",
                IsCompleted = true,
                DailyStartDate = new DateTime(utcNow.UtcDateTime.Year, utcNow.UtcDateTime.Month, utcNow.UtcDateTime.Day, 0, 0, 0, DateTimeKind.Utc),
                DailyRepeatType = (int)DailyRepeatType.Daily,
                DailyRepeatInterval = 1,
                DailyLastCompletedOn = new DateTime(utcNow.UtcDateTime.Year, utcNow.UtcDateTime.Month, utcNow.UtcDateTime.Day, 0, 0, 0, DateTimeKind.Utc),
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow
            },
            new BoardItemEntity { Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Todo, Title = "Submit assignment", IsCompleted = false, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow },
            new BoardItemEntity { Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Todo, Title = "Call advisor", IsCompleted = false, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow }
        );

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static BoardItem ToModel(BoardItemEntity entity)
    {
        DateOnly? start = null;
        DateOnly? todoDue = null;
        if (entity.Section == BoardSection.Daily)
        {
            start = entity.DailyStartDate is { } d0 ? DateOnly.FromDateTime(d0) : null;
        }
        else if (entity.Section == BoardSection.Todo)
        {
            todoDue = entity.DailyStartDate is { } d1 ? DateOnly.FromDateTime(d1) : null;
        }

        var repeat = Enum.IsDefined(typeof(DailyRepeatType), entity.DailyRepeatType)
            ? (DailyRepeatType)entity.DailyRepeatType
            : DailyRepeatType.Daily;
        int interval = entity.DailyRepeatInterval < 1 ? 1 : Math.Min(999, entity.DailyRepeatInterval);
        DateOnly? lastCompleted = entity.DailyLastCompletedOn is { } lc
            ? DateOnly.FromDateTime(lc)
            : null;
        DateOnly today = DailySchedule.UtcToday;
        bool isCompleted;
        if (entity.Section == BoardSection.Daily)
        {
            isCompleted = IsDailyEntityCompleteForToday(entity, today);
        }
        else
        {
            isCompleted = entity.IsCompleted;
        }

        return new(
            entity.Id,
            entity.Title,
            isCompleted,
            entity.Counter,
            entity.Notes,
            entity.Tags,
            entity.TrackPlus,
            entity.TrackMinus,
            entity.NegativeCounter,
            Enum.IsDefined(typeof(HabitResetPeriod), entity.ResetPeriod)
                ? (HabitResetPeriod)entity.ResetPeriod
                : HabitResetPeriod.Daily,
            start,
            entity.Section == BoardSection.Daily ? repeat : DailyRepeatType.Daily,
            entity.Section == BoardSection.Daily ? interval : 1,
            entity.ChecklistJson,
            lastCompleted,
            todoDue);
    }

    private static bool IsDailyEntityCompleteForToday(BoardItemEntity entity, DateOnly today)
    {
        if (entity.DailyLastCompletedOn is { } t && DateOnly.FromDateTime(t) == today)
        {
            return true;
        }

        return entity.DailyLastCompletedOn is null && entity.IsCompleted;
    }

    private void AddActivityEvent(Guid userId, ActivityEventType type, Guid? boardItemId, int? durationSeconds = null)
    {
        _dbContext.UserActivityEvents.Add(new UserActivityEventEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            EventType = type,
            BoardItemId = boardItemId,
            DurationSeconds = type == ActivityEventType.TimerSession ? durationSeconds : null
        });
    }
}
