using System.Collections.Frozen;

using App.Shared.RCL;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

internal sealed record DailyBackfillArgs(
    DateOnly? DailyStart,
    DailyRepeatType Repeat,
    int Interval,
    int Streak);

public sealed class BoardPersistenceService(
    ApplicationDbContext dbContext,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    BoardSnapshotCache snapshotCache,
    DailyStreakMapCache streakCache,
    IBoardChangeNotifier boardChangeNotifier,
    IUserTimeZoneService timeZone) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private async Task<T> LockAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LockAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await action();
        }
        finally
        {
            _gate.Release();
        }
    }

    private DateOnly Today() => DailySchedule.LocalToday(timeZone);

    private static IQueryable<BoardItemEntity> LiveBoardItems(ApplicationDbContext db, Guid userId) =>
        db.BoardItems.Where(x => x.UserId == userId && x.DeletedAtUtc == null && !x.IsArchived);

    private static BoardMutationStatus MatchExpected(BoardItemEntity? entity, DateTimeOffset? expectedUpdatedAtUtc)
    {
        if (entity is null)
        {
            return BoardMutationStatus.NotFound;
        }

        if (expectedUpdatedAtUtc is null)
        {
            return BoardMutationStatus.Ok;
        }

        return entity.UpdatedAtUtc.Equals(expectedUpdatedAtUtc.Value)
            ? BoardMutationStatus.Ok
            : BoardMutationStatus.Conflict;
    }

    public async Task<BoardSyncDelta> GetSyncDeltaAsync(
        Guid userId,
        DateTimeOffset cursorExclusive,
        CancellationToken cancellationToken = default)
    {
        await using var readDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        Dictionary<Guid, int> dailyStreaks = [];

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

    public async Task<BoardItem?> GetItemAsync(Guid userId, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardSnapshot cached = await GetSnapshotAsync(userId, cancellationToken);
        BoardItem? item = cached.Habits.FirstOrDefault(x => x.Id == itemId)
            ?? cached.Dailies.FirstOrDefault(x => x.Id == itemId)
            ?? cached.Todos.FirstOrDefault(x => x.Id == itemId);
        if (item is not null)
        {
            return item;
        }

        await using var readDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        BoardItemEntity? entity = await readDb.BoardItems.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.Id == itemId && x.DeletedAtUtc == null && !x.IsArchived,
                cancellationToken);
        return entity is null ? null : await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
    }

    public async Task<BoardSnapshot> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (snapshotCache.TryGet(userId, out var cached))
        {
            return cached;
        }

        await using var readDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var items = await LiveBoardItems(readDb, userId)
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var today = Today();
        var dailies = items.Where(x => x.Section == BoardSection.Daily).ToList();
        Dictionary<Guid, int> dailyStreaks = [];
        var snapshot = new BoardSnapshot(
            [.. items.Where(x => x.Section == BoardSection.Habit)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => ToModelWithToday(x, today, dailyStreaks))],
            [.. dailies
                .OrderBy(x => IsDailyEntityCompleteForToday(x, today) ? 1 : 0)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => ToModelWithToday(x, today, dailyStreaks))],
            [.. items.Where(x => x.Section == BoardSection.Todo)
                .OrderBy(x => x.IsCompleted)
                .ThenBy(x => x.DailyStartDate == null ? 0 : 1)
                .ThenBy(x => x.DailyStartDate ?? DateTime.MaxValue)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => ToModelWithToday(x, today, dailyStreaks))]);
        snapshotCache.Set(userId, snapshot);
        return snapshot;
    }

    public async Task<Dictionary<Guid, int>> GetDailyStreakMapAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (streakCache.TryGet(userId, out var cached))
        {
            return cached;
        }

        await using var readDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dailies = await LiveBoardItems(readDb, userId).AsNoTracking()
            .Where(x => x.Section == BoardSection.Daily)
            .ToListAsync(cancellationToken);
        var today = Today();
        var map = await BuildDailyStreakMapAsync(userId, dailies, today, readDb, cancellationToken);
        var result = new Dictionary<Guid, int>(map);
        streakCache.Set(userId, result);
        return result;
    }

    public Task<BoardItem> CreateItemAsync(Guid userId, BoardSection section, string title,
        Guid? itemId = null,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var utcNow = DateTimeOffset.UtcNow;
            if (itemId is { } id)
            {
                var existing = await dbContext.BoardItems
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.Id == id, cancellationToken);
                if (existing is not null)
                {
                    existing.DeletedAtUtc = null;
                    existing.Title = ZalgoSanitizer.SanitizeAndTrim(title);
                    existing.UpdatedAtUtc = utcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    var restored = await ToModelWithDailyStreaksAsync(userId, existing, cancellationToken);
                    await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
                    return restored;
                }
            }

            var entity = new BoardItemEntity
            {
                Id = itemId ?? Guid.NewGuid(),
                UserId = userId,
                Section = section,
                Title = ZalgoSanitizer.SanitizeAndTrim(title),
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
                UpdatedAtUtc = utcNow,
                SortOrder = await GetInitialSortOrderAsync(userId, section, cancellationToken)
            };

            dbContext.BoardItems.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
            var created = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
            await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
            return created;
        }, cancellationToken);

    public Task<BoardMutationResult> RenameItemAsync(
        Guid userId,
        BoardSection section,
        Guid itemId,
        string title,
        DateTimeOffset? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var entity = await dbContext.BoardItems
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.Section == section && x.Id == itemId && x.DeletedAtUtc == null,
                    cancellationToken);
            var st = MatchExpected(entity, expectedUpdatedAtUtc);
            if (st == BoardMutationStatus.NotFound)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            if (st == BoardMutationStatus.Conflict)
            {
                return new BoardMutationResult(
                    BoardMutationStatus.Conflict,
                    await ToModelWithDailyStreaksAsync(userId, entity!, cancellationToken));
            }

            entity!.Title = ZalgoSanitizer.SanitizeAndTrim(title);
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            var renamed = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
            await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
            return new BoardMutationResult(BoardMutationStatus.Ok, renamed);
        }, cancellationToken);

    public Task<BoardMutationResult> DeleteItemAsync(
        Guid userId,
        BoardSection section,
        Guid itemId,
        DateTimeOffset? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var entity = await dbContext.BoardItems
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.Section == section && x.Id == itemId && x.DeletedAtUtc == null,
                    cancellationToken);
            var st = MatchExpected(entity, expectedUpdatedAtUtc);
            if (st == BoardMutationStatus.NotFound)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            if (st == BoardMutationStatus.Conflict)
            {
                return new BoardMutationResult(
                    BoardMutationStatus.Conflict,
                    await ToModelWithDailyStreaksAsync(userId, entity!, cancellationToken));
            }

            var utc = DateTimeOffset.UtcNow;
            entity!.DeletedAtUtc = utc;
            entity.UpdatedAtUtc = utc;
            await dbContext.SaveChangesAsync(cancellationToken);
            await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
            return new BoardMutationResult(BoardMutationStatus.Ok, null);
        }, cancellationToken);

    public Task<BoardMutationResult> ArchiveItemAsync(
        Guid userId,
        BoardSection section,
        Guid itemId,
        DateTimeOffset? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var entity = await dbContext.BoardItems
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.Section == section && x.Id == itemId && x.DeletedAtUtc == null,
                    cancellationToken);
            var st = MatchExpected(entity, expectedUpdatedAtUtc);
            if (st == BoardMutationStatus.NotFound)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            if (st == BoardMutationStatus.Conflict)
            {
                return new BoardMutationResult(
                    BoardMutationStatus.Conflict,
                    await ToModelWithDailyStreaksAsync(userId, entity!, cancellationToken));
            }

            entity!.IsArchived = true;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            var archived = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
            await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
            return new BoardMutationResult(BoardMutationStatus.Ok, archived);
        }, cancellationToken);

    public Task<BoardMutationResult> UnarchiveItemAsync(
        Guid userId,
        BoardSection section,
        Guid itemId,
        DateTimeOffset? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var entity = await dbContext.BoardItems
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.Section == section && x.Id == itemId && x.DeletedAtUtc == null,
                    cancellationToken);
            var st = MatchExpected(entity, expectedUpdatedAtUtc);
            if (st == BoardMutationStatus.NotFound)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            if (st == BoardMutationStatus.Conflict)
            {
                return new BoardMutationResult(
                    BoardMutationStatus.Conflict,
                    await ToModelWithDailyStreaksAsync(userId, entity!, cancellationToken));
            }

            entity!.IsArchived = false;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            var unarchived = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
            await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
            return new BoardMutationResult(BoardMutationStatus.Ok, unarchived);
        }, cancellationToken);

    public async Task<BoardSnapshot> GetArchivedSnapshotAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var readDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var items = await readDb.BoardItems
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.DeletedAtUtc == null && x.IsArchived)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var today = Today();
        Dictionary<Guid, int> dailyStreaks = [];
        return new BoardSnapshot(
            [.. items.Where(x => x.Section == BoardSection.Habit)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => ToModelWithToday(x, today, dailyStreaks))],
            [.. items.Where(x => x.Section == BoardSection.Daily)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => ToModelWithToday(x, today, dailyStreaks))],
            [.. items.Where(x => x.Section == BoardSection.Todo)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => ToModelWithToday(x, today, dailyStreaks))]);
    }

    public Task<BoardMutationResult> CompleteDailyForDateAsync(
        Guid userId,
        Guid itemId,
        DateOnly completedOn,
        DateTimeOffset? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var today = Today();
            if (completedOn >= today)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            var entity = await dbContext.BoardItems
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.Section == BoardSection.Daily && x.Id == itemId
                         && x.DeletedAtUtc == null,
                    cancellationToken);
            if (entity is null)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            var st = MatchExpected(entity, expectedUpdatedAtUtc);
            if (st == BoardMutationStatus.Conflict)
            {
                return new BoardMutationResult(
                    BoardMutationStatus.Conflict,
                    await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));
            }

            var model = ToModelForDailyCheck(entity, today);
            if (!DailySchedule.IsDueOnDate(model, completedOn))
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            if (model.DailyLastCompletedOn == today)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            entity.DailyLastCompletedOn = completedOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            entity.IsCompleted = true;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddActivityEvent(userId, ActivityEventType.DailyComplete, itemId, null, entity.Title,
                DailyStreakCalculator.BackdatedDailyEventOccurredAt(completedOn));
            await dbContext.SaveChangesAsync(cancellationToken);
            var streakMap = await SyncDailyStreakCounterToComputedAsync(userId, entity, cancellationToken);
            var completed = ToModelWithDailyStreaksAsync(entity, streakMap);
            await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
            return new BoardMutationResult(BoardMutationStatus.Ok, completed);
        }, cancellationToken);

    public Task<BoardMutationResult> ToggleItemAsync(
        Guid userId,
        BoardSection section,
        Guid itemId,
        DateTimeOffset? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var entity = await dbContext.BoardItems
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.Section == section && x.Id == itemId && x.DeletedAtUtc == null,
                    cancellationToken);
            if (entity is null || section == BoardSection.Habit)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            var st = MatchExpected(entity, expectedUpdatedAtUtc);
            if (st == BoardMutationStatus.Conflict)
            {
                return new BoardMutationResult(
                    BoardMutationStatus.Conflict,
                    await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));
            }

            if (section == BoardSection.Daily)
            {
                ToggleDaily(entity, Today(), userId, itemId);
            }
            else
            {
                ToggleTodo(entity, userId, itemId);
            }

            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            IReadOnlyDictionary<Guid, int>? streakMap = null;
            if (section == BoardSection.Daily)
            {
                streakMap = await SyncDailyStreakCounterToComputedAsync(userId, entity, cancellationToken);
            }

            var toggled = streakMap is not null
                ? ToModelWithDailyStreaksAsync(entity, streakMap)
                : await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
            await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
            return new BoardMutationResult(BoardMutationStatus.Ok, toggled);
        }, cancellationToken);

    /// <summary>
    ///     Persists <see cref="BoardItemEntity.Counter" /> to the event-derived streak so the board, edit dialog,
    ///     and statistics stay aligned after check/uncheck (avoids Max(computed, counter) sticking on an old manual value).
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, int>> SyncDailyStreakCounterToComputedAsync(
        Guid userId,
        BoardItemEntity dailyEntity,
        CancellationToken cancellationToken)
    {
        var today = Today();
        var singleDailyList = new List<BoardItemEntity> { dailyEntity };
        var map = await BuildDailyStreakMapAsync(userId, singleDailyList, today, dbContext, cancellationToken);
        if (map.TryGetValue(dailyEntity.Id, out var streak))
        {
            dailyEntity.Counter = streak;
            dailyEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return map;
    }

    public Task LogTimerSessionAsync(
        Guid userId,
        TimeSpan duration,
        Guid? boardItemId,
        string? customLabel = null,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var sec = (int)Math.Min(int.MaxValue, Math.Max(0, duration.TotalSeconds));
            if (sec == 0)
            {
                return;
            }

            AddActivityEvent(userId, ActivityEventType.TimerSession, boardItemId, sec, customLabel);
            await dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

    public Task<BoardMutationResult> IncrementHabitPlusAsync(
        Guid userId,
        Guid itemId,
        DateTimeOffset? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var entity = await dbContext.BoardItems
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId && x.DeletedAtUtc == null,
                    cancellationToken);
            if (entity is null || !entity.TrackPlus)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            var st = MatchExpected(entity, expectedUpdatedAtUtc);
            if (st == BoardMutationStatus.Conflict)
            {
                return new BoardMutationResult(
                    BoardMutationStatus.Conflict,
                    await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));
            }

            entity.Counter++;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddActivityEvent(userId, ActivityEventType.HabitPlus, itemId, customLabel: entity.Title);
            await dbContext.SaveChangesAsync(cancellationToken);
            var afterPlus = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
            await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
            return new BoardMutationResult(BoardMutationStatus.Ok, afterPlus);
        }, cancellationToken);

    public Task<BoardMutationResult> IncrementHabitMinusAsync(
        Guid userId,
        Guid itemId,
        DateTimeOffset? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var entity = await dbContext.BoardItems
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId && x.DeletedAtUtc == null,
                    cancellationToken);
            if (entity is null || !entity.TrackMinus)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            var st = MatchExpected(entity, expectedUpdatedAtUtc);
            if (st == BoardMutationStatus.Conflict)
            {
                return new BoardMutationResult(
                    BoardMutationStatus.Conflict,
                    await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));
            }

            entity.NegativeCounter++;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddActivityEvent(userId, ActivityEventType.HabitMinus, itemId, customLabel: entity.Title);
            await dbContext.SaveChangesAsync(cancellationToken);
            var afterMinus = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
            await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
            return new BoardMutationResult(BoardMutationStatus.Ok, afterMinus);
        }, cancellationToken);

    public Task<BoardMutationResult> UpdateHabitAsync(
        Guid userId,
        Guid itemId,
        UpdateHabitArgs args,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var entity = await dbContext.BoardItems
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId && x.DeletedAtUtc == null,
                    cancellationToken);
            if (entity is null)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            var st = MatchExpected(entity, args.ExpectedUpdatedAtUtc);
            if (st == BoardMutationStatus.Conflict)
            {
                return new BoardMutationResult(
                    BoardMutationStatus.Conflict,
                    await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));
            }

            var trackPlus = args.TrackPlus;
            var trackMinus = args.TrackMinus;
            if (!trackPlus && !trackMinus)
            {
                trackPlus = true;
                trackMinus = true;
            }

            entity.Title = ZalgoSanitizer.SanitizeAndTrim(args.Title);
            entity.Notes = string.IsNullOrWhiteSpace(args.Notes) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Notes);
            entity.Tags = string.IsNullOrWhiteSpace(args.Tags) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Tags);
            entity.TrackPlus = trackPlus;
            entity.TrackMinus = trackMinus;
            entity.ResetPeriod = (int)args.ResetPeriod;
            entity.Counter = Math.Max(0, args.Counter);
            entity.NegativeCounter = Math.Max(0, args.NegativeCounter);
            entity.ChecklistJson = string.IsNullOrWhiteSpace(args.ChecklistJson)
                ? null
                : DailyChecklistJson.Serialize(DailyChecklistJson.Parse(args.ChecklistJson));

            await UpdateSortOrderIfNeededAsync(userId, BoardSection.Habit, entity, args.SortOrder, cancellationToken);

            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            var habit = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
            await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
            return new BoardMutationResult(BoardMutationStatus.Ok, habit);
        }, cancellationToken);

    public Task<BoardMutationResult> UpdateTodoAsync(
        Guid userId,
        Guid itemId,
        UpdateTodoArgs args,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var entity = await dbContext.BoardItems
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.Section == BoardSection.Todo && x.Id == itemId && x.DeletedAtUtc == null,
                    cancellationToken);
            if (entity is null)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            var st = MatchExpected(entity, args.ExpectedUpdatedAtUtc);
            if (st == BoardMutationStatus.Conflict)
            {
                return new BoardMutationResult(
                    BoardMutationStatus.Conflict,
                    await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));
            }

            DateTime? dueUtc = args.DueDate is { } d
                ? new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc)
                : null;

            entity.Title = ZalgoSanitizer.SanitizeAndTrim(args.Title);
            entity.Notes = string.IsNullOrWhiteSpace(args.Notes) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Notes);
            entity.Tags = string.IsNullOrWhiteSpace(args.Tags) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Tags);
            entity.ChecklistJson = string.IsNullOrWhiteSpace(args.ChecklistJson)
                ? null
                : DailyChecklistJson.Serialize(DailyChecklistJson.Parse(args.ChecklistJson));
            entity.DailyStartDate = dueUtc;

            await UpdateSortOrderIfNeededAsync(userId, BoardSection.Todo, entity, args.SortOrder, cancellationToken);

            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            var todo = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
            await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
            return new BoardMutationResult(BoardMutationStatus.Ok, todo);
        }, cancellationToken);

    public Task<BoardMutationResult> UpdateDailyAsync(
        Guid userId,
        Guid itemId,
        UpdateDailyArgs args,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            var entity = await dbContext.BoardItems
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.Section == BoardSection.Daily && x.Id == itemId && x.DeletedAtUtc == null,
                    cancellationToken);
            if (entity is null)
            {
                return new BoardMutationResult(BoardMutationStatus.NotFound, null);
            }

            var st = MatchExpected(entity, args.ExpectedUpdatedAtUtc);
            if (st == BoardMutationStatus.Conflict)
            {
                return new BoardMutationResult(
                    BoardMutationStatus.Conflict,
                    await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken));
            }

            var today = Today();
            var wasCompleteForToday = IsDailyEntityCompleteForToday(entity, today);

            var n = Math.Max(1, Math.Min(999, args.RepeatInterval));
            DateTime? startUtc = args.StartDate is { } s
                ? new DateTime(s.Year, s.Month, s.Day, 0, 0, 0, DateTimeKind.Utc)
                : null;
            var streakClamped = Math.Max(0, Math.Min(9999, args.Streak));

            DateOnly? newStartD = startUtc is { } su ? DateOnly.FromDateTime(su) : null;

            entity.Title = ZalgoSanitizer.SanitizeAndTrim(args.Title);
            entity.Notes = string.IsNullOrWhiteSpace(args.Notes) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Notes);
            entity.Tags = string.IsNullOrWhiteSpace(args.Tags) ? null : ZalgoSanitizer.SanitizeAndTrim(args.Tags);
            entity.DailyStartDate = startUtc;
            entity.DailyRepeatType = (int)args.RepeatType;
            entity.DailyRepeatInterval = n;
            entity.ChecklistJson = string.IsNullOrWhiteSpace(args.ChecklistJson)
                ? null
                : DailyChecklistJson.Serialize(DailyChecklistJson.Parse(args.ChecklistJson));
            entity.Counter = streakClamped;

            await UpdateSortOrderIfNeededAsync(userId, BoardSection.Daily, entity, args.SortOrder, cancellationToken);

            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

            // Always reconcile streak backfill, not only when Counter/schedule appear to change. Otherwise a save
            // with the same values (e.g. only title changed) or a previously skipped run leaves no DailyComplete
            // rows, so statistics/heatmap never match the daily streak.
            var streakNotAfter = today.AddDays(-1);
            await ReconcileDailyStreakBackfillAsync(userId, itemId,
                new DailyBackfillArgs(newStartD, args.RepeatType, n, streakClamped),
                streakNotAfter, cancellationToken);
            ApplyManualStreakToEntity(entity, newStartD, args.RepeatType, n, streakClamped, today, wasCompleteForToday);

            await dbContext.SaveChangesAsync(cancellationToken);
            var daily = await ToModelWithDailyStreaksAsync(userId, entity, cancellationToken);
            await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
            return new BoardMutationResult(BoardMutationStatus.Ok, daily);
        }, cancellationToken);

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

    private async Task ReconcileDailyStreakBackfillAsync(
        Guid userId,
        Guid itemId,
        DailyBackfillArgs args,
        DateOnly notAfter,
        CancellationToken cancellationToken)
    {
        var newSet = new HashSet<DateOnly>(DailyStreakBackfill.GetLastNScheduledCompletionDays(
            args.DailyStart, args.Repeat, args.Interval, args.Streak, notAfter));

        var toRemove = await dbContext.UserActivityEvents
            .Where(e => e.UserId == userId && e.BoardItemId == itemId && e.EventType == ActivityEventType.DailyComplete)
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

    private static readonly IReadOnlyDictionary<Guid, int> EmptyDailyStreaks =
        FrozenDictionary<Guid, int>.Empty;

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
        {
            return ToModelWithToday(entity, today, EmptyDailyStreaks);
        }

        await using var readDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var singleDailyList = new List<BoardItemEntity> { entity };
        var streaks = await BuildDailyStreakMapAsync(userId, singleDailyList, today, readDb, cancellationToken);
        return ToModelWithToday(entity, today, streaks);
    }

    private BoardItem ToModelWithDailyStreaksAsync(
        BoardItemEntity entity,
        IReadOnlyDictionary<Guid, int> streaks)
    {
        var today = Today();
        return ToModelWithToday(entity, today, streaks);
    }

    private static async Task<IReadOnlyDictionary<Guid, int>> BuildDailyStreakMapAsync(
        Guid userId,
        List<BoardItemEntity> dailies,
        DateOnly today,
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

        var historyStartUtc = new DateTimeOffset(
            minHistoryStart.Value.Year,
            minHistoryStart.Value.Month,
            minHistoryStart.Value.Day,
            0,
            0,
            0,
            TimeSpan.Zero);
        var endUtcExclusive = new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, TimeSpan.Zero)
            .AddDays(1);

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

        return ComputeDailyStreaks(dailies, today, byItem);
    }

    private static DateOnly? FindMinHistoryStart(List<BoardItemEntity> dailies, DateOnly today)
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

    private static Dictionary<Guid, int> ComputeDailyStreaks(
        List<BoardItemEntity> dailies,
        DateOnly today,
        Dictionary<Guid, List<(DateTimeOffset, ActivityEventType)>> byItem)
    {
        var outMap = new Dictionary<Guid, int>(dailies.Count);
        foreach (var ent in dailies)
        {
            byItem.TryGetValue(ent.Id, out var evList);
            var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(evList ?? []);
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
        repeat = Enum.IsDefined((DailyRepeatType)entity.DailyRepeatType)
            ? (DailyRepeatType)entity.DailyRepeatType
            : DailyRepeatType.Daily;
        interval = entity.DailyRepeatInterval < 1 ? 1 : Math.Min(999, entity.DailyRepeatInterval);
    }

    private static BoardItem ToModelForDailyCheck(BoardItemEntity entity, DateOnly today)
    {
        return ToModelWithToday(entity, today, EmptyDailyStreaks);
    }

    private static (DateOnly? start, DateOnly? todoDue) ResolveDates(BoardItemEntity entity)
    {
        if (entity.Section == BoardSection.Daily)
        {
            return (entity.DailyStartDate is { } d0 ? DateOnly.FromDateTime(d0) : null, null);
        }
        if (entity.Section == BoardSection.Todo)
        {
            return (null, entity.DailyStartDate is { } d1 ? DateOnly.FromDateTime(d1) : null);
        }
        return (null, null);
    }

    private static (DailyRepeatType repeat, int interval) ResolveSchedule(BoardItemEntity entity)
    {
        if (entity.Section != BoardSection.Daily)
        {
            return (DailyRepeatType.Daily, 1);
        }

        DailyRepeatType repeat = Enum.IsDefined((DailyRepeatType)entity.DailyRepeatType)
            ? (DailyRepeatType)entity.DailyRepeatType
            : DailyRepeatType.Daily;
        int interval = entity.DailyRepeatInterval < 1 ? 1 : Math.Min(999, entity.DailyRepeatInterval);
        return (repeat, interval);
    }

    private static HabitResetPeriod ResolveResetPeriod(BoardItemEntity entity)
    {
        return Enum.IsDefined((HabitResetPeriod)entity.ResetPeriod)
            ? (HabitResetPeriod)entity.ResetPeriod
            : HabitResetPeriod.Daily;
    }

    private static BoardItem ToModelWithToday(
        BoardItemEntity entity,
        DateOnly today,
        IReadOnlyDictionary<Guid, int> dailyStreakById)
    {
        var (start, todoDue) = ResolveDates(entity);
        var (repeat, interval) = ResolveSchedule(entity);
        DateOnly? lastCompleted = entity.DailyLastCompletedOn is { } lc
            ? DateOnly.FromDateTime(lc)
            : null;
        bool isCompleted = entity.Section == BoardSection.Daily
            ? IsDailyEntityCompleteForToday(entity, today)
            : entity.IsCompleted;

        int displayCounter;
        if (entity.Section == BoardSection.Daily)
        {
            displayCounter = dailyStreakById.TryGetValue(entity.Id, out int computedStreak)
                ? computedStreak
                : entity.Counter;
        }
        else
        {
            displayCounter = entity.Counter;
        }

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
            ResolveResetPeriod(entity),
            start,
            repeat,
            interval,
            entity.ChecklistJson,
            lastCompleted,
            todoDue,
            entity.UpdatedAtUtc,
            entity.CreatedAtUtc,
            entity.SortOrder,
            entity.IsArchived);
    }

    private static bool IsDailyEntityCompleteForToday(BoardItemEntity entity, DateOnly today)
    {
        if (entity.DailyLastCompletedOn is { } t && DateOnly.FromDateTime(t) == today)
        {
            return true;
        }

        return entity.DailyLastCompletedOn is null && entity.IsCompleted;
    }

    private async Task UpdateSortOrderIfNeededAsync(
        Guid userId,
        BoardSection section,
        BoardItemEntity entity,
        double? sortOrder,
        CancellationToken cancellationToken)
    {
        if (!sortOrder.HasValue)
        {
            return;
        }

        entity.SortOrder = sortOrder.Value;
        var needsRebalance = await dbContext.BoardItems
            .AnyAsync(x => x.UserId == userId
                        && x.Section == section
                        && x.DeletedAtUtc == null
                        && x.Id != entity.Id
                        && Math.Abs(x.SortOrder - sortOrder.Value) < 1e-9,
                      cancellationToken);
        if (needsRebalance)
        {
            await RebalanceSortOrdersAsync(userId, section, cancellationToken);
        }
    }

    private void ToggleDaily(BoardItemEntity entity, DateOnly today, Guid userId, Guid itemId)
    {
        var wasCompleteForToday = IsDailyEntityCompleteForToday(entity, today);
        if (wasCompleteForToday)
        {
            entity.DailyLastCompletedOn = null;
            entity.IsCompleted = false;
        }
        else
        {
            entity.DailyLastCompletedOn = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            entity.IsCompleted = true;
        }

        AddActivityEvent(userId,
            wasCompleteForToday ? ActivityEventType.DailyUncomplete : ActivityEventType.DailyComplete,
            itemId,
            customLabel: entity.Title);
    }

    private void ToggleTodo(BoardItemEntity entity, Guid userId, Guid itemId)
    {
        var wasCompleted = entity.IsCompleted;
        entity.IsCompleted = !entity.IsCompleted;
        AddActivityEvent(userId,
            wasCompleted ? ActivityEventType.TodoUncomplete : ActivityEventType.TodoComplete,
            itemId,
            customLabel: entity.Title);
    }

    private async Task<double> GetInitialSortOrderAsync(Guid userId, BoardSection section, CancellationToken cancellationToken)
    {
        var min = await dbContext.BoardItems
            .Where(x => x.UserId == userId && x.Section == section && x.DeletedAtUtc == null)
            .Select(x => (double?)x.SortOrder)
            .MinAsync(cancellationToken);
        return BoardItemReorder.SortOrderForNewItem(min);
    }

    private async Task RebalanceSortOrdersAsync(Guid userId, BoardSection section, CancellationToken cancellationToken)
    {
        var items = await dbContext.BoardItems
            .Where(x => x.UserId == userId && x.Section == section && x.DeletedAtUtc == null)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        double seq = 1.0;
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var item in items)
        {
            item.SortOrder = seq;
            item.UpdatedAtUtc = utcNow;
            seq += 1.0;
        }
    }


    public Task LogActivityAsync(
        Guid userId,
        ActivityEventType type,
        Guid? boardItemId,
        int? durationSeconds = null,
        string? itemTitleSnapshot = null,
        CancellationToken cancellationToken = default) =>
        LockAsync(async () =>
        {
            AddActivityEvent(userId, type, boardItemId, durationSeconds, itemTitleSnapshot);
            await dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

    private void AddActivityEvent(
        Guid userId,
        ActivityEventType type,
        Guid? boardItemId,
        int? durationSeconds = null,
        string? customLabel = null,
        DateTimeOffset? occurredAtUtc = null)
    {
        dbContext.UserActivityEvents.Add(new UserActivityEventEntity
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

    public void Dispose()
    {
        _gate.Dispose();
    }
}
