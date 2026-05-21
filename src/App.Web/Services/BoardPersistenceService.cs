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
    private readonly IUserTimeZoneService _timeZone;

    public BoardPersistenceService(
        ApplicationDbContext dbContext,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IBoardChangeNotifier boardChangeNotifier,
        IUserTimeZoneService timeZone)
    {
        _dbContext = dbContext;
        _dbContextFactory = dbContextFactory;
        _boardChangeNotifier = boardChangeNotifier;
        _timeZone = timeZone;
    }

    private DateOnly Today() => DailySchedule.LocalToday(_timeZone);

    private static IQueryable<BoardItemEntity> LiveBoardItems(ApplicationDbContext db, Guid userId) =>
        db.BoardItems.Where(x => x.UserId == userId && x.DeletedAtUtc == null);

    private static BoardMutationStatus MatchExpected(BoardItemEntity? entity, DateTimeOffset? expectedUpdatedAtUtc)
    {
        if (entity is null) return BoardMutationStatus.NotFound;
        if (expectedUpdatedAtUtc is null) return BoardMutationStatus.Ok;
        return entity.UpdatedAtUtc.Equals(expectedUpdatedAtUtc.Value)
            ? BoardMutationStatus.Ok
            : BoardMutationStatus.Conflict;
    }

    public async Task<BoardSyncDelta> GetSyncDeltaAsync(
        Guid userId,
        DateTimeOffset cursorExclusive,
        CancellationToken cancellationToken = default)
    {
        await using var readDb = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var changed = await readDb.BoardItems
            .AsNoTracking()
            .Where(x => x.UserId == userId
                        && ((x.DeletedAtUtc == null && x.UpdatedAtUtc > cursorExclusive)
                            || (x.DeletedAtUtc != null && x.DeletedAtUtc > cursorExclusive)))
            .OrderBy(x => x.UpdatedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var upserts = new List<BoardSyncItem>();
        var deletedIds = new List<Guid>();
        DateTimeOffset? next = null;

        var today = Today();
        var dailies = changed.Where(x => x.Section == BoardSection.Daily && x.DeletedAtUtc is null).ToList();
        var dailyStreaks = await BuildDailyStreakMapAsync(userId, dailies, today, readDb, cancellationToken);

        foreach (var row in changed)
        {
            if (row.DeletedAtUtc is not null)
            {
                deletedIds.Add(row.Id);
                next = MaxCursor(next, row.DeletedAtUtc.Value);
                continue;
            }

            upserts.Add(new BoardSyncItem(row.Section, ToModelWithToday(row, today, dailyStreaks)));
            next = MaxCursor(next, row.UpdatedAtUtc);
        }

        var nextCursor = (next ?? cursorExclusive).ToString("O");
        return new BoardSyncDelta(upserts, deletedIds, nextCursor);
    }

    private static DateTimeOffset? MaxCursor(DateTimeOffset? a, DateTimeOffset b) => a is null || b > a ? b : a;

    public async Task<BoardSnapshot> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Snapshot must reflect the database, not a long-lived scoped context's tracked copies (Blazor Server circuit).
        // Use a fresh context for reads so this cannot interleave on the same instance as a concurrent write
        // (Blazor re-entrancy: BoardChanged can trigger GetSnapshot while CreateItem is still in progress).
        await using var readDb = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var items = await LiveBoardItems(readDb, userId)
            .AsNoTracking()
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var today = Today();
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
        var r = await RenameItemForApiAsync(userId, section, itemId, title, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardMutationResult> RenameItemForApiAsync(
        Guid userId,
        BoardSection section,
        Guid itemId,
        string title,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.Section == section && x.Id == itemId && x.DeletedAtUtc == null,
                cancellationToken);
        var st = MatchExpected(entity, expectedUpdatedAtUtc);
        if (st == BoardMutationStatus.NotFound) return new BoardMutationResult(BoardMutationStatus.NotFound, null);
        if (st == BoardMutationStatus.Conflict)
            return new BoardMutationResult(
                BoardMutationStatus.Conflict,
                await ToModelWithDailyStreaksAsync(userId, entity!, cancellationToken));

        entity!.Title = title;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        var renamed = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return new BoardMutationResult(BoardMutationStatus.Ok, renamed);
    }

    public async Task<bool> DeleteItemAsync(Guid userId, BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var r = await DeleteItemForApiAsync(userId, section, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok;
    }

    public async Task<BoardMutationResult> DeleteItemForApiAsync(
        Guid userId,
        BoardSection section,
        Guid itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.Section == section && x.Id == itemId && x.DeletedAtUtc == null,
                cancellationToken);
        var st = MatchExpected(entity, expectedUpdatedAtUtc);
        if (st == BoardMutationStatus.NotFound) return new BoardMutationResult(BoardMutationStatus.NotFound, null);
        if (st == BoardMutationStatus.Conflict)
            return new BoardMutationResult(
                BoardMutationStatus.Conflict,
                await ToModelWithDailyStreaksAsync(userId, entity!, cancellationToken));

        var utc = DateTimeOffset.UtcNow;
        entity!.DeletedAtUtc = utc;
        entity.UpdatedAtUtc = utc;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return new BoardMutationResult(BoardMutationStatus.Ok, null);
    }

    public async Task<BoardItem?> CompleteDailyForDateAsync(
        Guid userId,
        Guid itemId,
        DateOnly completedOn,
        CancellationToken cancellationToken = default)
    {
        var r = await CompleteDailyForDateForApiAsync(userId, itemId, completedOn, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardMutationResult> CompleteDailyForDateForApiAsync(
        Guid userId,
        Guid itemId,
        DateOnly completedOn,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var today = Today();
        if (completedOn >= today) return new BoardMutationResult(BoardMutationStatus.NotFound, null);

        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.Section == BoardSection.Daily && x.Id == itemId
                     && x.DeletedAtUtc == null,
                cancellationToken);
        if (entity is null) return new BoardMutationResult(BoardMutationStatus.NotFound, null);

        var st = MatchExpected(entity, expectedUpdatedAtUtc);
        if (st == BoardMutationStatus.Conflict)
            return new BoardMutationResult(
                BoardMutationStatus.Conflict,
                await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));

        var model = ToModelForDailyCheck(entity, today);
        if (!DailySchedule.IsDueOnDate(model, completedOn))
            return new BoardMutationResult(BoardMutationStatus.NotFound, null);

        if (model.DailyLastCompletedOn == today) return new BoardMutationResult(BoardMutationStatus.NotFound, null);

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
        AddActivityEvent(userId, ActivityEventType.DailyComplete, itemId, null, entity.Title,
            DailyStreakCalculator.BackdatedDailyEventOccurredAt(completedOn));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await SyncDailyStreakCounterToComputedAsync(userId, entity, cancellationToken);
        var completed = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return new BoardMutationResult(BoardMutationStatus.Ok, completed);
    }

    public async Task<BoardItem?> ToggleItemAsync(Guid userId, BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var r = await ToggleItemForApiAsync(userId, section, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardMutationResult> ToggleItemForApiAsync(
        Guid userId,
        BoardSection section,
        Guid itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.Section == section && x.Id == itemId && x.DeletedAtUtc == null,
                cancellationToken);
        if (entity is null || section == BoardSection.Habit)
            return new BoardMutationResult(BoardMutationStatus.NotFound, null);

        var st = MatchExpected(entity, expectedUpdatedAtUtc);
        if (st == BoardMutationStatus.Conflict)
            return new BoardMutationResult(
                BoardMutationStatus.Conflict,
                await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));

        if (section == BoardSection.Daily)
        {
            var today = Today();
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
                wasCompleteForToday ? ActivityEventType.DailyUncomplete : ActivityEventType.DailyComplete,
                itemId,
                customLabel: entity.Title);
        }
        else
        {
            var wasCompleted = entity.IsCompleted;
            entity.IsCompleted = !entity.IsCompleted;
            AddActivityEvent(userId,
                wasCompleted ? ActivityEventType.TodoUncomplete : ActivityEventType.TodoComplete,
                itemId,
                customLabel: entity.Title);
        }

        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (section == BoardSection.Daily)
            await SyncDailyStreakCounterToComputedAsync(userId, entity, cancellationToken);

        var toggled = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return new BoardMutationResult(BoardMutationStatus.Ok, toggled);
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
        var dailies = await LiveBoardItems(_dbContext, userId).AsNoTracking()
            .Where(x => x.Section == BoardSection.Daily)
            .ToListAsync(cancellationToken);
        var today = Today();
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
        string? customLabel = null,
        CancellationToken cancellationToken = default)
    {
        var sec = (int)Math.Min(int.MaxValue, Math.Max(0, duration.TotalSeconds));
        if (sec == 0) return;

        AddActivityEvent(userId, ActivityEventType.TimerSession, boardItemId, sec, customLabel);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<BoardItem?> IncrementHabitPlusAsync(Guid userId, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var r = await IncrementHabitPlusForApiAsync(userId, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardMutationResult> IncrementHabitPlusForApiAsync(
        Guid userId,
        Guid itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId && x.DeletedAtUtc == null,
                cancellationToken);
        if (entity is null || !entity.TrackPlus)
            return new BoardMutationResult(BoardMutationStatus.NotFound, null);

        var st = MatchExpected(entity, expectedUpdatedAtUtc);
        if (st == BoardMutationStatus.Conflict)
            return new BoardMutationResult(
                BoardMutationStatus.Conflict,
                await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));

        entity.Counter++;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddActivityEvent(userId, ActivityEventType.HabitPlus, itemId, customLabel: entity.Title);
        await _dbContext.SaveChangesAsync(cancellationToken);
        var afterPlus = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return new BoardMutationResult(BoardMutationStatus.Ok, afterPlus);
    }

    public async Task<BoardItem?> IncrementHabitMinusAsync(Guid userId, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var r = await IncrementHabitMinusForApiAsync(userId, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardMutationResult> IncrementHabitMinusForApiAsync(
        Guid userId,
        Guid itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId && x.DeletedAtUtc == null,
                cancellationToken);
        if (entity is null || !entity.TrackMinus)
            return new BoardMutationResult(BoardMutationStatus.NotFound, null);

        var st = MatchExpected(entity, expectedUpdatedAtUtc);
        if (st == BoardMutationStatus.Conflict)
            return new BoardMutationResult(
                BoardMutationStatus.Conflict,
                await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));

        entity.NegativeCounter++;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddActivityEvent(userId, ActivityEventType.HabitMinus, itemId, customLabel: entity.Title);
        await _dbContext.SaveChangesAsync(cancellationToken);
        var afterMinus = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return new BoardMutationResult(BoardMutationStatus.Ok, afterMinus);
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
        var r = await UpdateHabitForApiAsync(
            userId,
            itemId,
            title,
            notes,
            tags,
            trackPlus,
            trackMinus,
            resetPeriod,
            counter,
            negativeCounter,
            checklistJson,
            null,
            cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardMutationResult> UpdateHabitForApiAsync(
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
        DateTimeOffset? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId && x.DeletedAtUtc == null,
                cancellationToken);
        if (entity is null) return new BoardMutationResult(BoardMutationStatus.NotFound, null);

        var st = MatchExpected(entity, expectedUpdatedAtUtc);
        if (st == BoardMutationStatus.Conflict)
            return new BoardMutationResult(
                BoardMutationStatus.Conflict,
                await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));

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
        return new BoardMutationResult(BoardMutationStatus.Ok, habit);
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
        var r = await UpdateTodoForApiAsync(
            userId,
            itemId,
            title,
            notes,
            tags,
            checklistJson,
            dueDate,
            null,
            cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardMutationResult> UpdateTodoForApiAsync(
        Guid userId,
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        string? checklistJson,
        DateTime? dueDate,
        DateTimeOffset? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.Section == BoardSection.Todo && x.Id == itemId && x.DeletedAtUtc == null,
                cancellationToken);
        if (entity is null) return new BoardMutationResult(BoardMutationStatus.NotFound, null);

        var st = MatchExpected(entity, expectedUpdatedAtUtc);
        if (st == BoardMutationStatus.Conflict)
            return new BoardMutationResult(
                BoardMutationStatus.Conflict,
                await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));

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
        return new BoardMutationResult(BoardMutationStatus.Ok, todo);
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
        var r = await UpdateDailyForApiAsync(
            userId,
            itemId,
            title,
            notes,
            tags,
            startDate,
            repeatType,
            repeatInterval,
            checklistJson,
            streak,
            null,
            cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardMutationResult> UpdateDailyForApiAsync(
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
        DateTimeOffset? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.Section == BoardSection.Daily && x.Id == itemId && x.DeletedAtUtc == null,
                cancellationToken);
        if (entity is null) return new BoardMutationResult(BoardMutationStatus.NotFound, null);

        var st = MatchExpected(entity, expectedUpdatedAtUtc);
        if (st == BoardMutationStatus.Conflict)
            return new BoardMutationResult(
                BoardMutationStatus.Conflict,
                await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));

        var today = Today();
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
        return new BoardMutationResult(BoardMutationStatus.Ok, daily);
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
        if (await LiveBoardItems(_dbContext, userId).AnyAsync(cancellationToken)) return;
        await InsertDemoBoardDataAsync(userId, cancellationToken);
    }

    /// <summary>Inserts the full demo board (habits, dailies, to-dos with tags, checklists, due dates).
    ///     Caller must ensure this user's board rows are cleared when replacing existing data.</summary>
    public async Task InsertDemoBoardDataAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var today = Today();
        var dayStart = new DateTime(today.Year, today.Month, today.Day, 0, 0, 0, DateTimeKind.Utc);
        var firstOfMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

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

        int order = 0;
        void AddBoardRow(BoardItemEntity row)
        {
            var t = utcNow.AddSeconds(order++);
            row.CreatedAtUtc = t;
            row.UpdatedAtUtc = t;
            _dbContext.BoardItems.Add(row);
        }

        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
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
            UserId = userId,
            Section = BoardSection.Habit,
            Title = "Read 10 minutes",
            Tags = "focus, learning",
            Counter = 2,
            NegativeCounter = 0
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
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
            UserId = userId,
            Section = BoardSection.Daily,
            Title = "Workout",
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
            UserId = userId,
            Section = BoardSection.Daily,
            Title = "Deep work block",
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
            UserId = userId,
            Section = BoardSection.Daily,
            Title = "Water the plants",
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
            UserId = userId,
            Section = BoardSection.Daily,
            Title = "Inbox review",
            Notes = "Monthly schedule — due on the same calendar day each month when it lands on a scheduled date.",
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
            UserId = userId,
            Section = BoardSection.Todo,
            Title = "Buy groceries",
            Tags = "home, errands",
            ChecklistJson = groceriesSub,
            IsCompleted = false,
            DailyStartDate = dayStart
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Section = BoardSection.Todo,
            Title = "Submit project draft",
            Notes = "Due in a few days — open the card to set another due date or subtasks.",
            Tags = "school, focus",
            IsCompleted = false,
            DailyStartDate = UtcDay(today.AddDays(3))
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Section = BoardSection.Todo,
            Title = "Skincare routine (evening)",
            Tags = "health, personal",
            ChecklistJson = skincareSub,
            IsCompleted = false,
            DailyStartDate = null
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Section = BoardSection.Todo,
            Title = "Return library books",
            Tags = "school, errands",
            IsCompleted = false,
            DailyStartDate = UtcDay(today.AddDays(-2))
        });
        AddBoardRow(new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
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
            UserId = userId,
            Section = BoardSection.Todo,
            Title = "File expense report",
            Tags = "work",
            IsCompleted = true,
            DailyStartDate = UtcDay(today.AddDays(-1))
        });

        for (var i = 0; i < 3; i++)
        {
            var d = today.AddDays(-(2 - i));
            _dbContext.UserActivityEvents.Add(new UserActivityEventEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OccurredAtUtc = DailyStreakCalculator.BackdatedDailyEventOccurredAt(d),
                EventType = ActivityEventType.DailyComplete,
                BoardItemId = dailyDeepId,
                CustomLabel = "Deep work block"
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
        var today = Today();
        if (entity.Section != BoardSection.Daily)
            return ToModelWithToday(entity, today, EmptyDailyStreaks);

        await using var readDb = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dailies = await LiveBoardItems(readDb, userId)
            .AsNoTracking()
            .Where(x => x.Section == BoardSection.Daily)
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
        DateOnly? minHistoryStart = null;
        foreach (var daily in dailies)
        {
            GetDailyEntitySchedule(daily, out var start, out var repeat, out var interval);
            var historyStart = DailySchedule.StreakHistoryScheduleStart(
                start,
                today,
                repeat,
                interval,
                DailyStreakCalculator.MaxStreak);
            minHistoryStart = minHistoryStart is null || historyStart < minHistoryStart
                ? historyStart
                : minHistoryStart;
        }

        var historyStartUtc = new DateTimeOffset(
            minHistoryStart!.Value.Year,
            minHistoryStart.Value.Month,
            minHistoryStart.Value.Day,
            0,
            0,
            0,
            TimeSpan.Zero);
        var endUtcExclusive = new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, TimeSpan.Zero)
            .AddDays(1);
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
            todoDue,
            entity.UpdatedAtUtc,
            entity.CreatedAtUtc);
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
        string? itemTitleSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        AddActivityEvent(userId, type, boardItemId, durationSeconds, itemTitleSnapshot);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private void AddActivityEvent(
        Guid userId,
        ActivityEventType type,
        Guid? boardItemId,
        int? durationSeconds = null,
        string? customLabel = null,
        DateTimeOffset? occurredAtUtc = null)
    {
        _dbContext.UserActivityEvents.Add(new UserActivityEventEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OccurredAtUtc = occurredAtUtc ?? DateTimeOffset.UtcNow,
            EventType = type,
            BoardItemId = boardItemId,
            DurationSeconds = type == ActivityEventType.TimerSession ? durationSeconds : null,
            CustomLabel = customLabel
        });
    }
}
