using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

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
    public void GroupDailyEventsByLocalDay_ShouldFilterAndGroupChronologically()
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

        var grouped = DailyStreakCalculator.GroupDailyEventsByLocalDay(events);

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

        // Today is not completed yet, so the streak should be 3, the 12th, 13th, and 14th
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

        // 13th is missing, not completed
        var events = new Dictionary<DateOnly, List<(DateTimeOffset, ActivityEventType)>>
        {
            { new DateOnly(2024, 5, 14), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 14, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } },
            { new DateOnly(2024, 5, 12), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 12, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } }
        };

        // Streak is only 1, yesterday, since the 13th was missed
        var streak = DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 1, today, events, null);
        streak.Should().Be(1);
    }

    [Fact]
    public void ComputeStreak_WhenYesterdayWasMissed_ShouldBeZero()
    {
        var start = new DateOnly(2024, 5, 10);
        var today = new DateOnly(2024, 5, 15);

        // Last completed was 13th. 14th, yesterday was missed and not completed.
        var lastCompleted = new DateOnly(2024, 5, 13);
        var events = new Dictionary<DateOnly, List<(DateTimeOffset, ActivityEventType)>>();

        var streak = DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 1, today, events, lastCompleted);
        streak.Should().Be(0);
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
        streak.Should().Be(2); // 13th from the event, 14th from lastCompleted
    }

    [Fact]
    public void ComputeStreak_WithIntervalRepeat_ShouldOnlyCountScheduledDays()
    {
        var start = new DateOnly(2024, 5, 10); // Friday
        var today = new DateOnly(2024, 5, 18); // Next Sunday

        // Repeat every 2 days. Scheduled days are the 10th, Fri, the 12th, Sun, the 14th, Tue, the 16th, Thu, the 18th, Sat and Sun.
        // Let's complete: 16th, 14th, 12th, 10th
        var events = new Dictionary<DateOnly, List<(DateTimeOffset, ActivityEventType)>>
        {
            { new DateOnly(2024, 5, 16), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 16, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } },
            { new DateOnly(2024, 5, 14), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 14, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } },
            { new DateOnly(2024, 5, 12), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 12, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } },
            { new DateOnly(2024, 5, 10), new List<(DateTimeOffset, ActivityEventType)> { (new DateTimeOffset(2024, 5, 10, 12, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete) } }
        };

        // 18th is scheduled but not completed yet. So streak runs from 16th back to 10th.
        // The days are the 16th, 14th, 12th, and 10th, 4 scheduled days completed
        var streak = DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 2, today, events, null);
        streak.Should().Be(4);
    }

    [Fact]
    public void GroupDailyEventsByLocalDay_EastOfUtc_AssignsMidnightCrossingToLocalDay()
    {
        var tz = new FixedOffsetTimeZoneService(TimeSpan.FromHours(2));
        // Local 00:30 on the 15th is UTC 22:30 on the 14th.
        var events = new[]
        {
            (new DateTimeOffset(2026, 4, 14, 22, 30, 0, TimeSpan.Zero), ActivityEventType.DailyComplete)
        };

        var local = DailyStreakCalculator.GroupDailyEventsByLocalDay(events, tz);
        local.Keys.Should().Equal(new DateOnly(2026, 4, 15));

        // Without a timezone the events fall on their UTC day, which masks the missed day.
        var utcFallback = DailyStreakCalculator.GroupDailyEventsByLocalDay(events);
        utcFallback.Keys.Should().Equal(new DateOnly(2026, 4, 14));
    }

    [Fact]
    public void GroupDailyEventsByLocalDay_WestOfUtc_AssignsLateEveningToLocalDay()
    {
        var tz = new FixedOffsetTimeZoneService(TimeSpan.FromHours(-8));
        // Local 23:00 on the 14th is UTC 07:00 on the 15th.
        var events = new[]
        {
            (new DateTimeOffset(2026, 4, 15, 7, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete)
        };

        var local = DailyStreakCalculator.GroupDailyEventsByLocalDay(events, tz);
        local.Keys.Should().Equal(new DateOnly(2026, 4, 14));
    }

    [Fact]
    public void GroupDailyEventsByLocalDay_AppliesDayStartRollback()
    {
        var dayStart = TimeSpan.FromHours(5);
        var events = new[]
        {
            (new DateTimeOffset(2026, 4, 15, 0, 30, 0, TimeSpan.Zero), ActivityEventType.DailyComplete)
        };

        var local = DailyStreakCalculator.GroupDailyEventsByLocalDay(events, dayStartLocalTime: dayStart);
        local.Keys.Should().Equal(new DateOnly(2026, 4, 14));
    }

    [Fact]
    public void ComputeStreak_MissedDayMaskedByUtcGrouping_EastOfUtc_BreaksWithLocalGrouping()
    {
        var start = new DateOnly(2026, 4, 1);
        var today = new DateOnly(2026, 4, 16); // local Thursday
        var tz = new FixedOffsetTimeZoneService(TimeSpan.FromHours(2));

        // Checked Tuesday the 14th at 23:00 local, missed Wednesday the 15th, checked Thursday the 16th at 00:30 local.
        // The 00:30 check is UTC 22:30 on the 15th, so UTC grouping places it on the missed day.
        var events = new[]
        {
            (new DateTimeOffset(2026, 4, 14, 21, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete),
            (new DateTimeOffset(2026, 4, 15, 22, 30, 0, TimeSpan.Zero), ActivityEventType.DailyComplete)
        };
        var lastCompleted = new DateOnly(2026, 4, 16);

        // Grouping without a timezone falls back to UTC days, which places the 00:30 check on the
        // missed day. The timezone-aware grouping keeps the streak broken.
        var utcFallback = DailyStreakCalculator.GroupDailyEventsByLocalDay(events);
        var localGrouped = DailyStreakCalculator.GroupDailyEventsByLocalDay(events, tz);

        DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 1, today, utcFallback, lastCompleted)
            .Should().Be(3, "UTC fallback grouping fills the missed day with the next check");
        DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 1, today, localGrouped, lastCompleted)
            .Should().Be(1, "local grouping keeps the streak broken");
    }

    [Fact]
    public void ComputeStreak_MissedDayMaskedByUtcGrouping_WestOfUtc_BreaksWithLocalGrouping()
    {
        var start = new DateOnly(2026, 4, 1);
        var today = new DateOnly(2026, 4, 16); // local Thursday
        var tz = new FixedOffsetTimeZoneService(TimeSpan.FromHours(-8));

        // Checked Tuesday the 14th at 23:00 local, missed Wednesday the 15th, checked Thursday the 16th at 23:00 local.
        // The Tue 23:00 check is UTC 07:00 on the 15th, so UTC grouping places it on the missed day.
        var events = new[]
        {
            (new DateTimeOffset(2026, 4, 15, 7, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete),
            (new DateTimeOffset(2026, 4, 17, 7, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete)
        };
        var lastCompleted = new DateOnly(2026, 4, 16);

        // Grouping without a timezone falls back to UTC days, which places the Tue evening check on
        // the missed day. The timezone-aware grouping keeps the streak broken.
        var utcFallback = DailyStreakCalculator.GroupDailyEventsByLocalDay(events);
        var localGrouped = DailyStreakCalculator.GroupDailyEventsByLocalDay(events, tz);

        DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 1, today, utcFallback, lastCompleted)
            .Should().Be(2, "UTC fallback grouping counts the missed day");
        DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 1, today, localGrouped, lastCompleted)
            .Should().Be(1, "local grouping keeps the streak broken");
    }

    [Fact]
    public void ComputeStreak_BackdatedRetroMarker_LandsOnTargetDay_ForAnyTimezone()
    {
        var start = new DateOnly(2026, 4, 1);
        var today = new DateOnly(2026, 4, 16);
        var tz = new FixedOffsetTimeZoneService(TimeSpan.FromHours(2));

        // Retro check-in for yesterday, logged at the fixed 15:00 UTC marker hour.
        var marker = DailyStreakCalculator.BackdatedDailyEventOccurredAt(today.AddDays(-1));
        var events = new[]
        {
            (marker, ActivityEventType.DailyComplete)
        };

        var localGrouped = DailyStreakCalculator.GroupDailyEventsByLocalDay(events, tz);
        localGrouped.Keys.Should().Equal(today.AddDays(-1));

        DailyStreakCalculator.ComputeStreak(start, DailyRepeatType.Daily, 1, today, localGrouped, today.AddDays(-1))
            .Should().Be(1);
    }
}
