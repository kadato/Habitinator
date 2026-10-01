using App.Shared.RCL.Models;

using FluentAssertions;

namespace App.Shared.Tests;

public class HabitResetScheduleTests
{
    [Fact]
    public void Daily_PeriodStart_IsToday()
    {
        var today = new DateOnly(2026, 10, 1);
        HabitResetSchedule.PeriodStartFor(today, HabitResetPeriod.Daily).Should().Be(today);
    }

    [Fact]
    public void Weekly_PeriodStart_IsMonday()
    {
        // 2026-10-01 is a Thursday; Monday is 2026-09-28.
        HabitResetSchedule.PeriodStartFor(new DateOnly(2026, 10, 1), HabitResetPeriod.Weekly)
            .Should().Be(new DateOnly(2026, 9, 28));
        HabitResetSchedule.PeriodStartFor(new DateOnly(2026, 9, 28), HabitResetPeriod.Weekly)
            .Should().Be(new DateOnly(2026, 9, 28));
        HabitResetSchedule.PeriodStartFor(new DateOnly(2026, 10, 4), HabitResetPeriod.Weekly)
            .Should().Be(new DateOnly(2026, 9, 28));
    }

    [Fact]
    public void Monthly_PeriodStart_IsFirstOfMonth()
    {
        HabitResetSchedule.PeriodStartFor(new DateOnly(2026, 10, 15), HabitResetPeriod.Monthly)
            .Should().Be(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void NeedsReset_NullAnchor_PreservesLegacyCounters()
    {
        HabitResetSchedule.NeedsReset(null, new DateOnly(2026, 10, 2), HabitResetPeriod.Daily)
            .Should().BeFalse();
    }

    [Fact]
    public void NeedsReset_StaleDailyAnchor_Resets()
    {
        HabitResetSchedule.NeedsReset(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), HabitResetPeriod.Daily)
            .Should().BeTrue();
        HabitResetSchedule.NeedsReset(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 2), HabitResetPeriod.Daily)
            .Should().BeFalse();
    }

    [Fact]
    public void NeedsReset_WeeklyAnchor_SameWeekKeeps()
    {
        var monday = new DateOnly(2026, 9, 28);
        HabitResetSchedule.NeedsReset(monday, new DateOnly(2026, 10, 1), HabitResetPeriod.Weekly)
            .Should().BeFalse();
        HabitResetSchedule.NeedsReset(monday, new DateOnly(2026, 10, 6), HabitResetPeriod.Weekly)
            .Should().BeTrue();
    }

    [Fact]
    public void EffectiveCounters_StalePeriod_ShowsZero()
    {
        var (counter, negative) = HabitResetSchedule.EffectiveCounters(
            5, 2, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), HabitResetPeriod.Monthly);
        counter.Should().Be(0);
        negative.Should().Be(0);
    }

    [Fact]
    public void EffectiveCounters_CurrentPeriod_KeepsValues()
    {
        var today = new DateOnly(2026, 10, 1);
        var (counter, negative) = HabitResetSchedule.EffectiveCounters(
            5, 2, today, today, HabitResetPeriod.Daily);
        counter.Should().Be(5);
        negative.Should().Be(2);
    }
}
