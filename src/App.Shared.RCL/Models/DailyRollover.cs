using App.Shared.RCL.Services;

namespace App.Shared.RCL.Models;

/// <summary>
/// Day-boundary rollover for dailies. The board itself is date-relative: a missed
/// daily simply appears due again on its next scheduled day, so misses never pile
/// up as overdue rows. This helper flags the misses that still deserve attention.
/// A miss from yesterday on an item due again today is normal rollover and stays
/// clean. Anything older, or anything that would otherwise vanish until its next
/// scheduled day, counts as overdue. Misses outside the window expire silently.
/// </summary>
public static class DailyRollover
{
    /// <summary>How far back overdue looks. Older misses expire instead of nagging.</summary>
    public const int MaxCatchUpDays = 7;

    /// <summary>
    /// Items with at least one overdue scheduled day inside the window, mapped to the
    /// oldest such date. Items checked off today are never overdue.
    /// </summary>
    public static IReadOnlyDictionary<Guid, DateOnly> GetOverdueSince(
        IReadOnlyList<BoardItem> dailies,
        DateOnly today,
        IUserTimeZoneService? tz = null,
        int maxDaysBack = MaxCatchUpDays)
    {
        ArgumentNullException.ThrowIfNull(dailies);
        var overdue = new Dictionary<Guid, DateOnly>();
        foreach (var item in dailies)
        {
            if (GetOldestMiss(item, today, tz, maxDaysBack) is { } missed)
            {
                overdue[item.Id] = missed;
            }
        }

        return overdue;
    }

    /// <summary>Oldest overdue scheduled day for one daily inside the window, or null when clean.</summary>
    public static DateOnly? GetOldestMiss(
        BoardItem daily,
        DateOnly today,
        IUserTimeZoneService? tz = null,
        int maxDaysBack = MaxCatchUpDays)
    {
        ArgumentNullException.ThrowIfNull(daily);
        var yesterday = today.AddDays(-1);
        var window = Math.Clamp(maxDaysBack, 1, MaxCatchUpDays);
        DateOnly? oldest = null;
        for (var back = 1; back <= window; back++)
        {
            var day = today.AddDays(-back);
            if (daily.DailyLastCompletedOn != today
                && !DidNotExistOnDay(daily, day, tz)
                && DailySchedule.IsDueOnDate(daily, day))
            {
                oldest = day;
            }
        }

        if (oldest == yesterday && DailySchedule.IsScheduledOn(daily, today))
        {
            return null;
        }

        return oldest;
    }

    private static bool DidNotExistOnDay(BoardItem item, DateOnly day, IUserTimeZoneService? tz)
    {
        if (item.DailyStartDate is not null || item.CreatedAtUtc is not { } createdUtc)
        {
            return false;
        }

        var local = tz is { IsDetected: true } ? tz.ConvertToLocal(createdUtc) : createdUtc;
        return DateOnly.FromDateTime(local.DateTime) > day;
    }
}
