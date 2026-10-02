using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

/// <summary>Builds the personal data export payload for a user.</summary>
public sealed class UserDataExportService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IUserTimeZoneService timeZone)
{
    public async Task<UserDataExportDto> BuildAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var (today, _) = await UserDayContext.LoadAsync(db, userId, timeZone, cancellationToken);

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

        return new UserDataExportDto(DateTimeOffset.UtcNow, [.. items.Select(e => Map(e, today))], events);
    }

    private static BoardItem Map(BoardItemEntity e, DateOnly today)
    {
        DateOnly? start = e.DailyStartDate is { } d ? DateOnly.FromDateTime(d) : null;
        DateOnly? lastCompleted = e.DailyLastCompletedOn is { } lc ? DateOnly.FromDateTime(lc) : null;
        var resetPeriod = Enum.IsDefined((HabitResetPeriod)e.ResetPeriod)
            ? (HabitResetPeriod)e.ResetPeriod
            : HabitResetPeriod.Daily;
        DateOnly? anchor = e.HabitPeriodStart is { } h ? DateOnly.FromDateTime(h) : null;

        var raw = new BoardItem(
            e.Id,
            e.Title,
            e.IsCompleted,
            e.Counter,
            e.Notes,
            e.Tags,
            e.TrackPlus,
            e.TrackMinus,
            e.NegativeCounter,
            resetPeriod,
            start,
            (DailyRepeatType)e.DailyRepeatType,
            e.DailyRepeatInterval,
            e.ChecklistJson,
            lastCompleted,
            e.Section == BoardSection.Todo ? start : null,
            e.UpdatedAtUtc,
            e.CreatedAtUtc,
            e.SortOrder,
            e.IsArchived,
            anchor,
            DailyWeekdays.Normalize(e.DailyWeekdays));
        return BoardItemMapper.WithLocalDay(raw, e.Section, today);
    }
}
