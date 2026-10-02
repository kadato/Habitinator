using App.Shared.RCL;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

/// <summary>Thrown when an export file fails validation. The endpoint maps it to a 400.</summary>
public sealed class UserDataImportValidationException(string message) : Exception(message);

/// <summary>
/// Restores a board from an export file. Items missing from the file become
/// tombstones so offline mirrors delete them through the normal delta pull.
/// Every touched row gets a fresh <c>UpdatedAtUtc</c> for the same reason.
/// Re-importing the same file yields the same board.
/// </summary>
public sealed class UserDataImportService(
    ApplicationDbContext dbContext,
    MemoryCacheStore<BoardSnapshot> snapshotCache,
    MemoryCacheStore<Dictionary<Guid, int>> streakCache,
    IBoardChangeNotifier boardChangeNotifier)
{
    public async Task<UserDataImportResult> ImportAsync(
        Guid userId,
        UserDataExportDto data,
        CancellationToken cancellationToken = default)
    {
        if (UserDataImportValidator.Validate(data) is { } problem)
        {
            throw new UserDataImportValidationException(problem);
        }

        var now = DateTimeOffset.UtcNow;
        var importIds = data.Items.Select(e => e.Item.Id).ToHashSet();

        await dbContext.BoardItems
            .Where(x => x.UserId == userId && x.DeletedAtUtc == null && !importIds.Contains(x.Id))
            .ExecuteUpdateAsync(
                x => x.SetProperty(e => e.DeletedAtUtc, now).SetProperty(e => e.UpdatedAtUtc, now),
                cancellationToken);

        foreach (var entry in data.Items)
        {
            var existing = await dbContext.BoardItems
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Id == entry.Item.Id, cancellationToken);
            if (existing is null)
            {
                dbContext.BoardItems.Add(ToEntity(userId, entry, now));
            }
            else
            {
                ApplyToEntity(existing, entry, now);
            }
        }

        await dbContext.UserActivityEvents
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        foreach (var record in data.Events)
        {
            dbContext.UserActivityEvents.Add(ToEventEntity(userId, record, importIds));
        }

        var prefsRestored = false;
        var notifRestored = false;
        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("Sign in required.");

        if (data.Preferences is not null)
        {
            user.UserPreferences = data.Preferences.Normalize();
            prefsRestored = true;
        }

        if (data.NotificationSettings is not null)
        {
            user.NotificationSettings = data.NotificationSettings;
            notifRestored = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        snapshotCache.Invalidate(userId);
        streakCache.Invalidate(userId);
        await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return new UserDataImportResult(data.Items.Count, data.Events.Count, prefsRestored, notifRestored);
    }

    private static BoardItemEntity ToEntity(Guid userId, BoardSyncItem entry, DateTimeOffset now)
    {
        var entity = new BoardItemEntity
        {
            Id = entry.Item.Id,
            UserId = userId,
            Section = entry.Section,
            CreatedAtUtc = entry.Item.CreatedAtUtc ?? now,
            UpdatedAtUtc = now,
        };
        ApplyToEntity(entity, entry, now);
        return entity;
    }

    private static void ApplyToEntity(BoardItemEntity entity, BoardSyncItem entry, DateTimeOffset now)
    {
        var item = entry.Item;
        var (trackPlus, trackMinus) = item.TrackPlus || item.TrackMinus
            ? (item.TrackPlus, item.TrackMinus)
            : (true, true);
        DateOnly? start = entry.Section == BoardSection.Todo ? item.TodoDueDate : item.DailyStartDate;

        entity.Section = entry.Section;
        entity.Title = ZalgoSanitizer.SanitizeAndTrim(item.Title);
        entity.Notes = string.IsNullOrWhiteSpace(item.Notes) ? null : ZalgoSanitizer.SanitizeAndTrim(item.Notes);
        entity.Tags = string.IsNullOrWhiteSpace(item.Tags) ? null : ZalgoSanitizer.SanitizeAndTrim(item.Tags);
        entity.TrackPlus = trackPlus;
        entity.TrackMinus = trackMinus;
        entity.ResetPeriod = (int)(Enum.IsDefined(item.ResetPeriod) ? item.ResetPeriod : HabitResetPeriod.Daily);
        entity.HabitPeriodStart = item.HabitPeriodStart?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        entity.IsCompleted = item.IsCompleted;
        entity.Counter = Math.Max(0, item.Counter);
        entity.NegativeCounter = Math.Max(0, item.NegativeCounter);
        entity.DailyStartDate = start?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        entity.DailyRepeatType = (int)(Enum.IsDefined(item.DailyRepeat) ? item.DailyRepeat : DailyRepeatType.Daily);
        entity.DailyRepeatInterval = Math.Max(1, Math.Min(999, item.DailyRepeatInterval));
        entity.DailyWeekdays = DailyWeekdays.Normalize(item.DailyWeekdays);
        entity.ChecklistJson = DailyChecklistJson.Normalize(item.ChecklistJson);
        entity.DailyLastCompletedOn = item.DailyLastCompletedOn?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        entity.SortOrder = item.SortOrder ?? 0;
        entity.DeletedAtUtc = null;
        entity.IsArchived = item.IsArchived;
        entity.UpdatedAtUtc = now;
    }

    private static UserActivityEventEntity ToEventEntity(
        Guid userId,
        UserActivityEventRecord record,
        HashSet<Guid> importIds)
    {
        return new UserActivityEventEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OccurredAtUtc = record.OccurredAtUtc,
            EventType = record.EventType,
            BoardItemId = record.BoardItemId is { } id && importIds.Contains(id) ? id : null,
            DurationSeconds = record.EventType == ActivityEventType.TimerSession ? record.DurationSeconds : null,
            CustomLabel = record.CustomLabel,
            EventId = null,
        };
    }
}
