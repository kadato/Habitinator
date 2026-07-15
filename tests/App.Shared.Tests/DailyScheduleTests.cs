using App.Shared.RCL.Models;

using FluentAssertions;

namespace App.Shared.Tests;

public class DailyScheduleTests
{
    [Fact]
    public void EveryNDays_Interval1_HitsAllDaysOnOrAfterStart()
    {
        var start = new DateOnly(2024, 1, 1);
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 1, new DateOnly(2024, 1, 1)).Should().BeTrue();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 1, new DateOnly(2024, 1, 2)).Should().BeTrue();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 1, new DateOnly(2024, 1, 3)).Should().BeTrue();
    }

    [Fact]
    public void EveryNDays_Interval2_SkipsAlternateDays()
    {
        var start = new DateOnly(2024, 1, 1);
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 2, new DateOnly(2024, 1, 1)).Should().BeTrue();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 2, new DateOnly(2024, 1, 2)).Should().BeFalse();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 2, new DateOnly(2024, 1, 3)).Should().BeTrue();
    }

    [Fact]
    public void BeforeStart_NeverScheduled()
    {
        var start = new DateOnly(2024, 2, 1);
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 1, new DateOnly(2024, 1, 31)).Should().BeFalse();
    }

    [Fact]
    public void Weekly_SameDow_Interval1()
    {
        var start = new DateOnly(2024, 1, 8);
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 15)).Should().BeTrue();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 9)).Should().BeFalse();
    }

    [Fact]
    public void DueRequiresCompletionCheck()
    {
        var d = new BoardItem(
            Guid.NewGuid(),
            "T",
            false,
            0,
            null,
            null,
            true,
            true,
            0,
            HabitResetPeriod.Daily,
            new DateOnly(2024, 1, 1),
            DailyRepeatType.Daily,
            1,
            null,
            null,
            null);
        DateOnly t = new(2024, 1, 1);
        DailySchedule.IsDueOnDate(d, t).Should().BeTrue();
        var done = d with { DailyLastCompletedOn = t };
        DailySchedule.IsDueOnDate(done, t).Should().BeFalse();
    }
}
