using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

/// <summary>Builds the personal data export payload for a user.</summary>
public sealed class UserDataExportService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<UserDataExportDto> BuildAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var items = await db.BoardItems.AsNoTracking()
            .Where(x => x.UserId == userId && x.DeletedAtUtc == null)
            .OrderBy(x => x.Section)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var events = await db.UserActivityEvents.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.OccurredAtUtc)
            .Select(e => new UserActivityEventRecord(e.OccurredAtUtc, e.EventType, e.BoardItemId, e.DurationSeconds, e.CustomLabel))
            .ToListAsync(cancellationToken);

        return new UserDataExportDto(DateTimeOffset.UtcNow, [.. items.Select(Map)], events);
    }

    private static BoardItem Map(BoardItemEntity e)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly? start = e.DailyStartDate is { } d ? DateOnly.FromDateTime(d) : null;
        DateOnly? lastCompleted = e.DailyLastCompletedOn is { } lc ? DateOnly.FromDateTime(lc) : null;
        var isCompleted = e.Section == BoardSection.Daily
            ? e.IsCompleted && lastCompleted == today
            : e.IsCompleted;

        return new BoardItem(
            e.Id,
            e.Title,
            isCompleted,
            e.Counter,
            e.Notes,
            e.Tags,
            e.TrackPlus,
            e.TrackMinus,
            e.NegativeCounter,
            (HabitResetPeriod)e.ResetPeriod,
            start,
            (DailyRepeatType)e.DailyRepeatType,
            e.DailyRepeatInterval,
            e.ChecklistJson,
            lastCompleted,
            e.Section == BoardSection.Todo ? start : null,
            e.TodoRepeatIntervalDays,
            e.UpdatedAtUtc,
            e.CreatedAtUtc,
            e.SortOrder,
            e.IsArchived);
    }
}
