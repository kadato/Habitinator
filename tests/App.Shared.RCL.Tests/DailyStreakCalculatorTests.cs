using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using FluentAssertions;

namespace App.Shared.RCL.Tests;

public sealed class DailyStreakCalculatorTests
{
    [Fact]
    public void Three_day_run_including_today_yields_3()
    {
        var today = new DateOnly(2026, 4, 27);
        var d1 = today.AddDays(-2);
        var d2 = today.AddDays(-1);
        var events = new List<(DateTimeOffset, ActivityEventType)>
        {
            (At(d1, 10), ActivityEventType.DailyComplete),
            (At(d2, 10), ActivityEventType.DailyComplete),
            (At(today, 10), ActivityEventType.DailyComplete)
        };
        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(events);
        var n = DailyStreakCalculator.ComputeStreak(
            d1,
            DailyRepeatType.Daily,
            1,
            today,
            grouped,
            today);
        n.Should().Be(3);
    }

    [Fact]
    public void Today_not_done_counts_from_yesterday_lenient()
    {
        var today = new DateOnly(2026, 4, 27);
        var y = today.AddDays(-1);
        var y2 = today.AddDays(-2);
        var events = new List<(DateTimeOffset, ActivityEventType)>
        {
            (At(y2, 10), ActivityEventType.DailyComplete),
            (At(y, 10), ActivityEventType.DailyComplete)
        };
        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(events);
        var n = DailyStreakCalculator.ComputeStreak(
            y2,
            DailyRepeatType.Daily,
            1,
            today,
            grouped,
            y);
        n.Should().Be(2);
    }

    [Fact]
    public void Null_start_today_not_done_still_counts_event_streak_through_yesterday()
    {
        var today = new DateOnly(2026, 4, 27);
        var y = today.AddDays(-1);
        var y2 = today.AddDays(-2);
        var events = new List<(DateTimeOffset, ActivityEventType)>
        {
            (At(y2, 12), ActivityEventType.DailyComplete),
            (At(y, 12), ActivityEventType.DailyComplete)
        };
        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(events);
        var n = DailyStreakCalculator.ComputeStreak(
            null,
            DailyRepeatType.Daily,
            1,
            today,
            grouped,
            y);
        n.Should().Be(2);
    }

    [Fact]
    public void Stored_start_today_today_not_done_counts_yesterday_from_events()
    {
        var today = new DateOnly(2026, 4, 27);
        var y = today.AddDays(-1);
        var y2 = today.AddDays(-2);
        var events = new List<(DateTimeOffset, ActivityEventType)>
        {
            (At(y2, 12), ActivityEventType.DailyComplete),
            (At(y, 12), ActivityEventType.DailyComplete)
        };
        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(events);
        var n = DailyStreakCalculator.ComputeStreak(
            today,
            DailyRepeatType.Daily,
            1,
            today,
            grouped,
            y);
        n.Should().Be(2);
    }

    [Fact]
    public void Today_not_scheduled_ends_chain_before_today()
    {
        // Tuesday not a "weekly" occurrence; only previous Monday counts; today (Wed) is not in streak anchor.
        var start = new DateOnly(2026, 4, 20); // Mon
        var today = new DateOnly(2026, 4, 22);  // Wed
        var mon = new DateOnly(2026, 4, 20);
        var events = new List<(DateTimeOffset, ActivityEventType)>
        {
            (At(mon, 10), ActivityEventType.DailyComplete)
        };
        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(events);
        var n = DailyStreakCalculator.ComputeStreak(
            start,
            DailyRepeatType.Weekly,
            1,
            today,
            grouped,
            null);
        n.Should().Be(1);
    }

    [Fact]
    public void Uncomplete_last_event_makes_day_not_done()
    {
        var d = new DateOnly(2026, 4, 25);
        var events = new List<(DateTimeOffset, ActivityEventType)>
        {
            (At(d, 9), ActivityEventType.DailyComplete),
            (At(d, 10), ActivityEventType.DailyUncomplete)
        };
        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(events);
        var n = DailyStreakCalculator.ComputeStreak(
            d,
            DailyRepeatType.Daily,
            1,
            d,
            grouped,
            null);
        n.Should().Be(0);
    }

    private static DateTimeOffset At(DateOnly day, int hourUtc) =>
        new(day.Year, day.Month, day.Day, hourUtc, 0, 0, TimeSpan.Zero);
}
