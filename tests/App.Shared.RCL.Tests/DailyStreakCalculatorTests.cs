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

    [Fact]
    public void Null_start_interval_daily_counts_every_completed_day()
    {
        // A null start means the board schedules every day, so an interval-2 daily the user checks
        // daily must count every consecutive day (previously only every other day was counted).
        var today = new DateOnly(2026, 4, 27);
        var completed = Enumerable.Range(0, 10).Select(i => today.AddDays(-1 - i)).ToArray();
        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(
            completed.Select(d => (At(d, 10), ActivityEventType.DailyComplete)));
        var n = DailyStreakCalculator.ComputeStreak(
            null, DailyRepeatType.Daily, 2, today, grouped, null);
        n.Should().Be(10);
    }

    [Fact]
    public void Start_today_interval3_counts_scheduled_days_when_today_done()
    {
        // Created today with an explicit start: the streak must follow the real schedule phase
        // (today, today-3, ...) instead of a misaligned synthetic anchor that yielded 0.
        var today = new DateOnly(2026, 4, 27);
        var completed = new[] { today, today.AddDays(-3), today.AddDays(-6) };
        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(
            completed.Select(d => (At(d, 10), ActivityEventType.DailyComplete)));
        var n = DailyStreakCalculator.ComputeStreak(
            today, DailyRepeatType.Daily, 3, today, grouped, null);
        n.Should().Be(3);
    }

    [Fact]
    public void Start_today_interval2_counts_scheduled_days_when_today_done()
    {
        var today = new DateOnly(2026, 4, 27);
        var completed = new[] { today, today.AddDays(-2) };
        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(
            completed.Select(d => (At(d, 10), ActivityEventType.DailyComplete)));
        var n = DailyStreakCalculator.ComputeStreak(
            today, DailyRepeatType.Daily, 2, today, grouped, null);
        n.Should().Be(2);
    }

    [Fact]
    public void Start_today_interval2_counts_scheduled_days_through_yesterday_when_today_open()
    {
        var today = new DateOnly(2026, 4, 27);
        var completed = new[] { today.AddDays(-2), today.AddDays(-4) };
        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(
            completed.Select(d => (At(d, 10), ActivityEventType.DailyComplete)));
        var n = DailyStreakCalculator.ComputeStreak(
            today, DailyRepeatType.Daily, 2, today, grouped, null);
        n.Should().Be(2);
    }

    [Fact]
    public void Start_today_weekly_counts_same_dow_chain_when_today_done()
    {
        var today = new DateOnly(2026, 4, 27); // Monday
        var completed = new[] { today, today.AddDays(-7) };
        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(
            completed.Select(d => (At(d, 10), ActivityEventType.DailyComplete)));
        var n = DailyStreakCalculator.ComputeStreak(
            today, DailyRepeatType.Weekly, 1, today, grouped, null);
        n.Should().Be(2);
    }

    [Fact]
    public void Retro_backdated_event_counts_toward_streak()
    {
        var yesterday = new DateOnly(2026, 4, 26);
        var events = new List<(DateTimeOffset, ActivityEventType)>
        {
            (At(yesterday, 20), ActivityEventType.DailyComplete),
            (DailyStreakCalculator.BackdatedDailyEventOccurredAt(yesterday), ActivityEventType.DailyComplete)
        };
        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(events);
        var n = DailyStreakCalculator.ComputeStreak(
            yesterday, DailyRepeatType.Daily, 1, new DateOnly(2026, 4, 27), grouped, yesterday);
        n.Should().Be(1);
    }

    private static DateTimeOffset At(DateOnly day, int hourUtc) =>
        new(day.Year, day.Month, day.Day, hourUtc, 0, 0, TimeSpan.Zero);
}
