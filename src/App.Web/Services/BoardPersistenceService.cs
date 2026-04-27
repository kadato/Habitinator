using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class BoardPersistenceService
{
    private readonly IBoardChangeNotifier _boardChangeNotifier;
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ApplicationDbContext _dbContext;

    public BoardPersistenceService(
        ApplicationDbContext dbContext,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IBoardChangeNotifier boardChangeNotifier)
    {
        _dbContext = dbContext;
        _dbContextFactory = dbContextFactory;
        _boardChangeNotifier = boardChangeNotifier;
    }

    public async Task<BoardSnapshot> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Snapshot must reflect the database, not a long-lived scoped context's tracked copies (Blazor Server circuit).
        // Use a fresh context for reads so this cannot interleave on the same instance as a concurrent write
        // (Blazor re-entrancy: BoardChanged can trigger GetSnapshot while CreateItem is still in progress).
        await using var readDb = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var items = await readDb.BoardItems
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var today = DailySchedule.UtcToday;
        var dailies = items.Where(x => x.Section == BoardSection.Daily).ToList();
        var dailyStreaks = await BuildDailyStreakMapAsync(userId, dailies, today, readDb, cancellationToken);
        return new BoardSnapshot(
            items.Where(x => x.Section == BoardSection.Habit)
                .OrderBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => ToModelWithToday(x, today, dailyStreaks))
                .ToList(),
            dailies
                .OrderBy(x => IsDailyEntityCompleteForToday(x, today) ? 1 : 0)
                .ThenBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => ToModelWithToday(x, today, dailyStreaks))
                .ToList(),
            items.Where(x => x.Section == BoardSection.Todo)
                .OrderBy(x => x.IsCompleted)
                .ThenBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => ToModelWithToday(x, today, dailyStreaks))
                .ToList());
    }

    public async Task<BoardItem> CreateItemAsync(Guid userId, BoardSection section, string title,
        CancellationToken cancellationToken = default)
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
            // Null = "due from UTC today" without blocking streak backfill / stats for prior days (see DailySchedule).
            DailyStartDate = null,
            DailyRepeatType = (int)DailyRepeatType.Daily,
            DailyRepeatInterval = 1,
            ChecklistJson = null,
            DailyLastCompletedOn = null,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };

        _dbContext.BoardItems.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        var created = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return created;
    }

    public async Task<BoardItem?> RenameItemAsync(Guid userId, BoardSection section, Guid itemId, string title,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == section && x.Id == itemId, cancellationToken);
        if (entity is null) return null;

        entity.Title = title;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        var renamed = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return renamed;
    }

    public async Task<bool> DeleteItemAsync(Guid userId, BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == section && x.Id == itemId, cancellationToken);
        if (entity is null) return false;

        _dbContext.BoardItems.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return true;
    }

    public async Task<BoardItem?> CompleteDailyForDateAsync(
        Guid userId,
        Guid itemId,
        DateOnly completedOn,
        CancellationToken cancellationToken = default)
    {
        var today = DailySchedule.UtcToday;
        if (completedOn >= today) return null;

        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.Section == BoardSection.Daily && x.Id == itemId,
                cancellationToken);
        if (entity is null) return null;

        var model = ToModelForDailyCheck(entity, today);
        if (!DailySchedule.IsDueOnDate(model, completedOn)) return null;

        if (model.DailyLastCompletedOn == today) return null;

        entity.DailyLastCompletedOn = new DateTime(
            completedOn.Year,
            completedOn.Month,
            completedOn.Day,
            0,
            0,
            0,
            DateTimeKind.Utc);
        entity.IsCompleted = true;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddActivityEvent(userId, ActivityEventType.DailyComplete, itemId, null,
            DailyStreakCalculator.BackdatedDailyEventOccurredAt(completedOn));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await SyncDailyStreakCounterToComputedAsync(userId, entity, cancellationToken);
        var completed = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return completed;
    }

    public async Task<BoardItem?> ToggleItemAsync(Guid userId, BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == section && x.Id == itemId, cancellationToken);
        if (entity is null || section == BoardSection.Habit) return null;

        if (section == BoardSection.Daily)
        {
            var today = DailySchedule.UtcToday;
            var wasCompleteForToday = IsDailyEntityCompleteForToday(entity, today);
            if (wasCompleteForToday)
            {
                entity.DailyLastCompletedOn = null;
                entity.IsCompleted = false;
            }
            else
            {
                entity.DailyLastCompletedOn =
                    new DateTime(today.Year, today.Month, today.Day, 0, 0, 0, DateTimeKind.Utc);
                entity.IsCompleted = true;
            }

            AddActivityEvent(userId,
                wasCompleteForToday ? ActivityEventType.DailyUncomplete : ActivityEventType.DailyComplete, itemId);
        }
        else
        {
            var wasCompleted = entity.IsCompleted;
            entity.IsCompleted = !entity.IsCompleted;
            AddActivityEvent(userId, wasCompleted ? ActivityEventType.TodoUncomplete : ActivityEventType.TodoComplete,
                itemId);
        }

        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (section == BoardSection.Daily)
            await SyncDailyStreakCounterToComputedAsync(userId, entity, cancellationToken);

        var toggled = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return toggled;
    }

    /// <summary>
    ///     Persists <see cref="BoardItemEntity.Counter" /> to the event-derived streak so the board, edit dialog,
    ///     and statistics stay aligned after check/uncheck (avoids Max(computed, counter) sticking on an old manual value).
    /// </summary>
    private async Task SyncDailyStreakCounterToComputedAsync(
        Guid userId,
        BoardItemEntity dailyEntity,
        CancellationToken cancellationToken)
    {
        var dailies = await _dbContext.BoardItems.AsNoTracking()
            .Where(x => x.UserId == userId && x.Section == BoardSection.Daily)
            .ToListAsync(cancellationToken);
        var today = DailySchedule.UtcToday;
        var map = await BuildDailyStreakMapAsync(userId, dailies, today, _dbContext, cancellationToken);
        if (map.TryGetValue(dailyEntity.Id, out var streak))
        {
            dailyEntity.Counter = streak;
            dailyEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task LogTimerSessionAsync(
        Guid userId,
        TimeSpan duration,
        Guid? boardItemId,
        CancellationToken cancellationToken = default)
    {
        var sec = (int)Math.Min(int.MaxValue, Math.Max(0, duration.TotalSeconds));
        if (sec == 0) return;

        AddActivityEvent(userId, ActivityEventType.TimerSession, boardItemId, sec);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<BoardItem?> IncrementHabitPlusAsync(Guid userId, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId,
                cancellationToken);
        if (entity is null || !entity.TrackPlus) return null;

        entity.Counter++;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddActivityEvent(userId, ActivityEventType.HabitPlus, itemId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        var afterPlus = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return afterPlus;
    }

    public async Task<BoardItem?> IncrementHabitMinusAsync(Guid userId, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId,
                cancellationToken);
        if (entity is null || !entity.TrackMinus) return null;

        entity.NegativeCounter++;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddActivityEvent(userId, ActivityEventType.HabitMinus, itemId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        var afterMinus = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return afterMinus;
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
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId,
                cancellationToken);
        if (entity is null) return null;

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
        var habit = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return habit;
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
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == BoardSection.Todo && x.Id == itemId,
                cancellationToken);
        if (entity is null) return null;

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
        var todo = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return todo;
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
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == BoardSection.Daily && x.Id == itemId,
                cancellationToken);
        if (entity is null) return null;

        var today = DailySchedule.UtcToday;
        var wasCompleteForToday = IsDailyEntityCompleteForToday(entity, today);

        var n = Math.Max(1, Math.Min(999, repeatInterval));
        DateTime? startUtc = startDate is { } s
            ? new DateTime(s.Year, s.Month, s.Day, 0, 0, 0, DateTimeKind.Utc)
            : null;
        var streakClamped = Math.Max(0, Math.Min(9999, streak));

        DateOnly? newStartD = startUtc is { } su ? DateOnly.FromDateTime(su) : null;

        entity.Title = title;
        entity.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        entity.Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim();
        entity.DailyStartDate = startUtc;
        entity.DailyRepeatType = (int)repeatType;
        entity.DailyRepeatInterval = n;
        entity.ChecklistJson = string.IsNullOrWhiteSpace(checklistJson) ? null : checklistJson.Trim();
        entity.Counter = streakClamped;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        // Always reconcile streak backfill, not only when Counter/schedule appear to change. Otherwise a save
        // with the same values (e.g. only title changed) or a previously skipped run leaves no DailyComplete
        // rows, so statistics/heatmap never match the daily streak.
        var streakNotAfter = today.AddDays(-1);
        await ReconcileDailyStreakBackfillAsync(userId, itemId, newStartD, repeatType, n, streakClamped,
            streakNotAfter, cancellationToken);
        ApplyManualStreakToEntity(entity, newStartD, repeatType, n, streakClamped, today, wasCompleteForToday);

        await _dbContext.SaveChangesAsync(cancellationToken);
        var daily = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return daily;
    }

    private static void ApplyManualStreakToEntity(
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
                entity.DailyLastCompletedOn =
                    new DateTime(today.Year, today.Month, today.Day, 0, 0, 0, DateTimeKind.Utc);
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
            entity.DailyLastCompletedOn =
                new DateTime(today.Year, today.Month, today.Day, 0, 0, 0, DateTimeKind.Utc);
            entity.IsCompleted = true;
        }
        else
        {
            entity.DailyLastCompletedOn =
                new DateTime(newest.Year, newest.Month, newest.Day, 0, 0, 0, DateTimeKind.Utc);
            entity.IsCompleted = false;
        }
    }

    private async Task ReconcileDailyStreakBackfillAsync(
        Guid userId,
        Guid itemId,
        DateOnly? dailyStart,
        DailyRepeatType repeat,
        int interval,
        int streak,
        DateOnly notAfter,
        CancellationToken cancellationToken)
    {
        var newSet = new HashSet<DateOnly>(DailyStreakBackfill.GetLastNScheduledCompletionDays(
            dailyStart, repeat, interval, streak, notAfter));

        var toRemove = await _dbContext.UserActivityEvents
            .Where(e => e.UserId == userId && e.BoardItemId == itemId && e.EventType == ActivityEventType.DailyComplete)
            .ToListAsync(cancellationToken);
        foreach (var e in toRemove)
        {
            if (!DailyStreakBackfill.IsStreakBackfillTimestamp(e.OccurredAtUtc)) continue;
            var day = DateOnly.FromDateTime(e.OccurredAtUtc.UtcDateTime);
            if (newSet.Contains(day)) continue;
            _dbContext.UserActivityEvents.Remove(e);
        }

        foreach (var d in newSet)
        {
            var dayStart = new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero);
            var dayEnd = dayStart.AddDays(1);
            var hasAny = await _dbContext.UserActivityEvents.AnyAsync(
                e => e.UserId == userId
                     && e.BoardItemId == itemId
                     && e.EventType == ActivityEventType.DailyComplete
                     && e.OccurredAtUtc >= dayStart
                     && e.OccurredAtUtc < dayEnd,
                cancellationToken);
            if (hasAny) continue;
            _dbContext.UserActivityEvents.Add(new UserActivityEventEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OccurredAtUtc = DailyStreakBackfill.StreakBackfillOccurredAt(d),
                EventType = ActivityEventType.DailyComplete,
                BoardItemId = itemId
            });
        }
    }

    public async Task SeedBoardDataIfMissingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var hasItems = await _dbContext.BoardItems.AnyAsync(x => x.UserId == userId, cancellationToken);
        if (hasItems) return;

        var utcNow = DateTimeOffset.UtcNow;
        var dayStart = new DateTime(utcNow.UtcDateTime.Year, utcNow.UtcDateTime.Month, utcNow.UtcDateTime.Day, 0, 0, 0,
            DateTimeKind.Utc);
        var dailyWorkoutId = Guid.NewGuid();
        var dailyDeepId = Guid.NewGuid();
        _dbContext.BoardItems.AddRange(
            new BoardItemEntity
            {
                Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Habit, Title = "Drink a glass of water",
                Counter = 3, NegativeCounter = 2, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow
            },
            new BoardItemEntity
            {
                Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Habit, Title = "Read 10 pages",
                Counter = 1, NegativeCounter = 0, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow
            },
            new BoardItemEntity
            {
                Id = dailyWorkoutId,
                UserId = userId,
                Section = BoardSection.Daily,
                Title = "Workout",
                IsCompleted = false,
                DailyStartDate = dayStart,
                DailyRepeatType = (int)DailyRepeatType.Daily,
                DailyRepeatInterval = 1,
                DailyLastCompletedOn = null,
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow
            },
            new BoardItemEntity
            {
                Id = dailyDeepId,
                UserId = userId,
                Section = BoardSection.Daily,
                Title = "Deep work block",
                IsCompleted = true,
                DailyStartDate = dayStart,
                DailyRepeatType = (int)DailyRepeatType.Daily,
                DailyRepeatInterval = 1,
                DailyLastCompletedOn = dayStart,
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow
            },
            new BoardItemEntity
            {
                Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Todo, Title = "Submit assignment",
                IsCompleted = false, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow
            },
            new BoardItemEntity
            {
                Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Todo, Title = "Call advisor",
                IsCompleted = false, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow
            }
        );

        var today = DateOnly.FromDateTime(utcNow.UtcDateTime);
        for (var i = 0; i < 3; i++)
        {
            var d = today.AddDays(-(2 - i));
            _dbContext.UserActivityEvents.Add(new UserActivityEventEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OccurredAtUtc = DailyStreakCalculator.BackdatedDailyEventOccurredAt(d),
                EventType = ActivityEventType.DailyComplete,
                BoardItemId = dailyDeepId
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
    }

    private static readonly IReadOnlyDictionary<Guid, int> EmptyDailyStreaks =
        new Dictionary<Guid, int>();

    /// <summary>Maps entity to API model (queries the DB for daily streaks). Streak queries use
    /// <see cref="IDbContextFactory{ApplicationDbContext}"/> so they do not overlap the scoped
    /// context; still await this before <see cref="IBoardChangeNotifier.NotifyBoardChangedAsync" /> for ordering.</summary>
    private async Task<BoardItem> ToModelWithDailyStreaksAsync(
        Guid userId,
        BoardItemEntity entity,
        CancellationToken cancellationToken = default)
    {
        var today = DailySchedule.UtcToday;
        await using var readDb = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dailies = await readDb.BoardItems
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Section == BoardSection.Daily)
            .ToListAsync(cancellationToken);
        var streaks = await BuildDailyStreakMapAsync(userId, dailies, today, readDb, cancellationToken);
        return ToModelWithToday(entity, today, streaks);
    }

    private async Task<IReadOnlyDictionary<Guid, int>> BuildDailyStreakMapAsync(
        Guid userId,
        IReadOnlyList<BoardItemEntity> dailies,
        DateOnly today,
        ApplicationDbContext forQueries,
        CancellationToken cancellationToken = default)
    {
        if (dailies.Count == 0) return new Dictionary<Guid, int>();
        var ids = dailies.Select(x => x.Id).ToList();
        var eventRows = await forQueries.UserActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId
                        && e.BoardItemId != null
                        && ids.Contains(e.BoardItemId.Value)
                        && (e.EventType == ActivityEventType.DailyComplete
                            || e.EventType == ActivityEventType.DailyUncomplete))
            .Select(e => new { e.BoardItemId, e.OccurredAtUtc, e.EventType })
            .ToListAsync(cancellationToken);
        var byItem = new Dictionary<Guid, List<(DateTimeOffset, ActivityEventType)>>();
        foreach (var e in eventRows)
        {
            if (e.BoardItemId is not { } bid) continue;
            if (!byItem.TryGetValue(bid, out var list))
            {
                list = new List<(DateTimeOffset, ActivityEventType)>();
                byItem[bid] = list;
            }

            list.Add((e.OccurredAtUtc, e.EventType));
        }

        var outMap = new Dictionary<Guid, int>(dailies.Count);
        foreach (var ent in dailies)
        {
            byItem.TryGetValue(ent.Id, out var evList);
            var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(evList ?? new List<(DateTimeOffset, ActivityEventType)>());
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

    private static void GetDailyEntitySchedule(
        BoardItemEntity entity,
        out DateOnly? start,
        out DailyRepeatType repeat,
        out int interval)
    {
        start = entity.DailyStartDate is { } d0 ? DateOnly.FromDateTime(d0) : null;
        repeat = Enum.IsDefined(typeof(DailyRepeatType), entity.DailyRepeatType)
            ? (DailyRepeatType)entity.DailyRepeatType
            : DailyRepeatType.Daily;
        interval = entity.DailyRepeatInterval < 1 ? 1 : Math.Min(999, entity.DailyRepeatInterval);
    }

    private static BoardItem ToModelForDailyCheck(BoardItemEntity entity, DateOnly today)
    {
        return ToModelWithToday(entity, today, EmptyDailyStreaks);
    }

    private static BoardItem ToModelWithToday(
        BoardItemEntity entity,
        DateOnly today,
        IReadOnlyDictionary<Guid, int> dailyStreakById)
    {
        DateOnly? start = null;
        DateOnly? todoDue = null;
        if (entity.Section == BoardSection.Daily)
            start = entity.DailyStartDate is { } d0 ? DateOnly.FromDateTime(d0) : null;
        else if (entity.Section == BoardSection.Todo)
            todoDue = entity.DailyStartDate is { } d1 ? DateOnly.FromDateTime(d1) : null;

        var repeat = Enum.IsDefined(typeof(DailyRepeatType), entity.DailyRepeatType)
            ? (DailyRepeatType)entity.DailyRepeatType
            : DailyRepeatType.Daily;
        var interval = entity.DailyRepeatInterval < 1 ? 1 : Math.Min(999, entity.DailyRepeatInterval);
        DateOnly? lastCompleted = entity.DailyLastCompletedOn is { } lc
            ? DateOnly.FromDateTime(lc)
            : null;
        bool isCompleted;
        if (entity.Section == BoardSection.Daily)
            isCompleted = IsDailyEntityCompleteForToday(entity, today);
        else
            isCompleted = entity.IsCompleted;

        // Dailies: Counter column holds the value from the edit dialog (and backfill). Event-derived streak
        // is the live picture; take the max so manual saves show up when they exceed computed (e.g. just set).
        var displayCounter = entity.Section == BoardSection.Daily
            ? Math.Max(dailyStreakById.GetValueOrDefault(entity.Id, 0), entity.Counter)
            : entity.Counter;

        return new BoardItem(
            entity.Id,
            entity.Title,
            isCompleted,
            displayCounter,
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
        if (entity.DailyLastCompletedOn is { } t && DateOnly.FromDateTime(t) == today) return true;

        return entity.DailyLastCompletedOn is null && entity.IsCompleted;
    }

    public async Task LogActivityAsync(
        Guid userId,
        ActivityEventType type,
        Guid? boardItemId,
        int? durationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        AddActivityEvent(userId, type, boardItemId, durationSeconds);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private void AddActivityEvent(
        Guid userId,
        ActivityEventType type,
        Guid? boardItemId,
        int? durationSeconds = null,
        DateTimeOffset? occurredAtUtc = null)
    {
        _dbContext.UserActivityEvents.Add(new UserActivityEventEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OccurredAtUtc = occurredAtUtc ?? DateTimeOffset.UtcNow,
            EventType = type,
            BoardItemId = boardItemId,
            DurationSeconds = type == ActivityEventType.TimerSession ? durationSeconds : null
        });
    }
}
