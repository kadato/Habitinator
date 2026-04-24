using App.Shared.RCL.Models;
using Xunit;

namespace App.Shared.Tests;

public class DailyScheduleTests
{
    [Fact]
    public void EveryNDays_Interval1_HitsAllDaysOnOrAfterStart()
    {
        var start = new DateOnly(2024, 1, 1);
        Assert.True(DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 1, new DateOnly(2024, 1, 1)));
        Assert.True(DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 1, new DateOnly(2024, 1, 2)));
        Assert.True(DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 1, new DateOnly(2024, 1, 3)));
    }

    [Fact]
    public void EveryNDays_Interval2_SkipsAlternateDays()
    {
        var start = new DateOnly(2024, 1, 1);
        Assert.True(DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 2, new DateOnly(2024, 1, 1)));
        Assert.False(DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 2, new DateOnly(2024, 1, 2)));
        Assert.True(DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 2, new DateOnly(2024, 1, 3)));
    }

    [Fact]
    public void BeforeStart_NeverScheduled()
    {
        var start = new DateOnly(2024, 2, 1);
        Assert.False(DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 1, new DateOnly(2024, 1, 31)));
    }

    [Fact]
    public void Weekly_SameDow_Interval1()
    {
        var start = new DateOnly(2024, 1, 8);
        Assert.True(DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 15)));
        Assert.False(DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 9)));
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
        Assert.True(DailySchedule.IsDueOnDate(d, t));
        var done = d with { DailyLastCompletedOn = t };
        Assert.False(DailySchedule.IsDueOnDate(done, t));
    }
}
