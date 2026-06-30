using System;
using System.Collections.Generic;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

using Xunit;

namespace App.Shared.Tests;

public class DailyStreakCalculatorTests
{
    [Fact]
    public void BackdatedDailyEventOccurredAt_ShouldReturnTargetTimeAt15Utc()
    {
        var day = new DateOnly(2024, 5, 20);
        var expected = new DateTimeOffset(2024, 5, 20, 15, 0, 0, TimeSpan.Zero);

        DailyStreakCalculator.BackdatedDailyEventOccurredAt(day).Should().Be(expected);
    }

    [Fact]
    public void IsCalendarDayNetCompleted_WithNoEvents_ShouldBeTrueIfMatchesLastCompleted()
    {
        var d = new DateOnly(2024, 5, 20);

        DailyStreakCalculator.IsCalendarDayNetCompleted(d, null, d).Should().BeTrue();
        DailyStreakCalculator.IsCalendarDayNetCompleted(d, null, d.AddDays(-1)).Should().BeFalse();
    }

    [Fact]
    public void IsCalendarDayNetCompleted_WithEvents_ShouldDependOnLastEvent()
    {
        var d = new DateOnly(2024, 5, 20);
        var dayOffset = new DateTimeOffset(2024, 5, 20, 10, 0, 0, TimeSpan.Zero);

        var eventsWithComplete = new List<(DateTimeOffset, ActivityEventType)>
        {
            (dayOffset, ActivityEventType.DailyUncomplete),
            (dayOffset.AddHours(1), ActivityEventType.DailyComplete)
        };

        var eventsWithUncomplete = new List<(DateTimeOffset, ActivityEventType)>
        {
            (dayOffset, ActivityEventType.DailyComplete),
            (dayOffset.AddHours(1), ActivityEventType.DailyUncomplete)
        };

        DailyStreakCalculator.IsCalendarDayNetCompleted(d, eventsWithComplete, null).Should().BeTrue();
        DailyStreakCalculator.IsCalendarDayNetCompleted(d, eventsWithUncomplete, null).Should().BeFalse();
    }

    [Fact]
    public void GroupDailyEventsByUtcDay_ShouldFilterAndGroupChronologically()
    {
        var t1 = new DateTimeOffset(2024, 5, 20, 8, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2024, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2024, 5, 21, 6, 0, 0, TimeSpan.Zero);
        var ignoreTime = new DateTimeOffset(2024, 5, 20, 10, 0, 0, TimeSpan.Zero);

        var events = new[]
        {
            (t2, ActivityEventType.DailyComplete),
            (ignoreTime, ActivityEventType.HabitPlus), // Should be filtered out
            (t1, ActivityEventType.DailyUncomplete),  // Older than t2, should be sorted first
            (t3, ActivityEventType.DailyComplete)
        };

        var grouped = DailyStreakCalculator.GroupDailyEventsByUtcDay(events);

        grouped.Should().HaveCount(2);

        var day1 = new DateOnly(2024, 5, 20);
        grouped.Should().ContainKey(day1);
        grouped[day1].Should().HaveCount(2);
        grouped[day1][0].OccurredAtUtc.Should().Be(t1);
        grouped[day1][0].Type.Should().Be(ActivityEventType.DailyUncomplete);
        grouped[day1][1].OccurredAtUtc.Should().Be(t2);
        grouped[day1][1].Type.Should().Be(ActivityEventType.DailyComplete);

        var day2 = new DateOnly(2024, 5, 21);
        grouped.Should().ContainKey(day2);
        grouped[day2].Should().HaveCount(1);
        grouped[day2][0].OccurredAtUtc.Should().Be(t3);
        grouped[day2][0].Type.Should().Be(ActivityEventType.DailyComplete);
    }

    [Fact]
    public void ComputeStreak_WhenTodayNotCompleted_ShouldComputeStreakThroughYesterday()
    {
        var start = new DateOnly(2024, 5, 10);
        var today = new DateOnly(2024, 5, 15);

        // Yesterday was completed
        var events = new Dictionary<DateOnly, List<(DateTimeOffset, ActivityEventType)>>
        {
            { new DateOnly(2024, 5, 14), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 14, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } },
            { new DateOnly(2024, 5, 13), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 13, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } },
            { new DateOnly(2024, 5, 12), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 12, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } }
        };

        // Today is not completed yet, so the streak should be 3 (12th, 13th, 14th)
        var streak = DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 1, today, events, null);
        streak.Should().Be(3);
    }

    [Fact]
    public void ComputeStreak_WhenTodayIsCompleted_ShouldIncludeToday()
    {
        var start = new DateOnly(2024, 5, 10);
        var today = new DateOnly(2024, 5, 15);

        // Today is completed via events
        var events = new Dictionary<DateOnly, List<(DateTimeOffset, ActivityEventType)>>
        {
            { today, new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 15, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } },
            { new DateOnly(2024, 5, 14), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 14, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } }
        };

        var streak = DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 1, today, events, null);
        streak.Should().Be(2);
    }

    [Fact]
    public void ComputeStreak_WhenBrokenByMissedScheduledDay_ShouldStopCounting()
    {
        var start = new DateOnly(2024, 5, 10);
        var today = new DateOnly(2024, 5, 15);

        // 13th is missing (not completed)
        var events = new Dictionary<DateOnly, List<(DateTimeOffset, ActivityEventType)>>
        {
            { new DateOnly(2024, 5, 14), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 14, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } },
            { new DateOnly(2024, 5, 12), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 12, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } }
        };

        // Streak is only 1 (yesterday, since 13th was missed)
        var streak = DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 1, today, events, null);
        streak.Should().Be(1);
    }

    [Fact]
    public void ComputeStreak_WithLastCompletedDateProperty_ShouldBeHonored()
    {
        var start = new DateOnly(2024, 5, 10);
        var today = new DateOnly(2024, 5, 15);

        // Yesterday completed via dailyLastCompletedOn property, previous days via events
        var lastCompleted = new DateOnly(2024, 5, 14);
        var events = new Dictionary<DateOnly, List<(DateTimeOffset, ActivityEventType)>>
        {
            { new DateOnly(2024, 5, 13), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 13, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } }
        };

        var streak = DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 1, today, events, lastCompleted);
        streak.Should().Be(2); // 13th (event) + 14th (lastCompleted)
    }

    [Fact]
    public void ComputeStreak_WithIntervalRepeat_ShouldOnlyCountScheduledDays()
    {
        var start = new DateOnly(2024, 5, 10); // Friday
        var today = new DateOnly(2024, 5, 18); // Next Sunday

        // Repeat every 2 days. Scheduled days are: 10th (Fri), 12th (Sun), 14th (Tue), 16th (Thu), 18th (Sat/Sun)
        // Let's complete: 16th, 14th, 12th, 10th
        var events = new Dictionary<DateOnly, List<(DateTimeOffset, ActivityEventType)>>
        {
            { new DateOnly(2024, 5, 16), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 16, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } },
            { new DateOnly(2024, 5, 14), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 14, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } },
            { new DateOnly(2024, 5, 12), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 12, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } },
            { new DateOnly(2024, 5, 10), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 10, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } }
        };

        // 18th is scheduled but not completed yet. So streak runs from 16th back to 10th.
        // The days are 16th, 14th, 12th, 10th (4 scheduled days completed)
        var streak = DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 2, today, events, null);
        streak.Should().Be(4);
    }
}
