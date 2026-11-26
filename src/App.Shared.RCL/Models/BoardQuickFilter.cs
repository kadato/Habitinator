using App.Shared.RCL.Services;

namespace App.Shared.RCL.Models;

/// <summary>One-tap board filters for due now, overdue, and this week.</summary>
public enum BoardQuickFilter
{
    All,
    DueNow,
    Overdue,
    ThisWeek
}

public static class BoardQuickFilterRules
{
    public static bool Matches(
        BoardSection section,
        BoardItem item,
        BoardQuickFilter filter,
        DateOnly today,
        IUserTimeZoneService? timeZone = null)
    {
        if (filter == BoardQuickFilter.All)
        {
            return true;
        }

        return section switch
        {
            BoardSection.Habit => filter != BoardQuickFilter.Overdue,
            BoardSection.Daily => MatchesDaily(item, filter, today, timeZone),
            BoardSection.Todo => MatchesTodo(item, filter, today),
            _ => true
        };
    }

    private static bool MatchesDaily(
        BoardItem item,
        BoardQuickFilter filter,
        DateOnly today,
        IUserTimeZoneService? timeZone)
    {
        var overdue = DailyRollover.GetOldestMiss(item, today, timeZone) is not null;
        return filter switch
        {
            BoardQuickFilter.DueNow => !item.IsCompleted && (overdue || DailySchedule.IsDueOnDate(item, today)),
            BoardQuickFilter.Overdue => overdue,
            BoardQuickFilter.ThisWeek => IsScheduledWithinWeek(item, today),
            _ => true
        };
    }

    private static bool MatchesTodo(BoardItem item, BoardQuickFilter filter, DateOnly today) =>
        filter switch
        {
            BoardQuickFilter.DueNow => !item.IsCompleted && item.TodoDueDate is { } due && due <= today,
            BoardQuickFilter.Overdue => !item.IsCompleted && item.TodoDueDate is { } due && due < today,
            BoardQuickFilter.ThisWeek => item.TodoDueDate is { } due
                && due >= today
                && due <= today.AddDays(6),
            _ => true
        };

    private static bool IsScheduledWithinWeek(BoardItem item, DateOnly today)
    {
        for (var offset = 0; offset < 7; offset++)
        {
            if (DailySchedule.IsScheduledOrPostponedOn(item, today.AddDays(offset)))
            {
                return true;
            }
        }

        return false;
    }
}
