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

    [Fact]
    public void GetYesterdayUncompletedDailies_NewDailyCreatedToday_IsNotListed()
    {
        var today = new DateOnly(2026, 4, 27);
        var item = NewDaily("Created today", Utc(today));
        DailySchedule.GetYesterdayUncompletedDailies([item], today).Should().BeEmpty();
    }

    [Fact]
    public void GetYesterdayUncompletedDailies_ExistingNullStartDaily_IsListed()
    {
        var today = new DateOnly(2026, 4, 27);
        var item = NewDaily("Existing", Utc(today.AddDays(-7)));
        DailySchedule.GetYesterdayUncompletedDailies([item], today)
            .Select(x => x.Id).Should().Equal(item.Id);
    }

    [Fact]
    public void GetYesterdayUncompletedDailies_CompletedToday_IsNotListed()
    {
        var today = new DateOnly(2026, 4, 27);
        var item = NewDaily("Done today", Utc(today.AddDays(-7))) with { DailyLastCompletedOn = today };
        DailySchedule.GetYesterdayUncompletedDailies([item], today).Should().BeEmpty();
    }

    [Fact]
    public void GetYesterdayUncompletedDailies_CompletedYesterday_IsNotListed()
    {
        var today = new DateOnly(2026, 4, 27);
        var item = NewDaily("Done yesterday", Utc(today.AddDays(-7))) with { DailyLastCompletedOn = today.AddDays(-1) };
        DailySchedule.GetYesterdayUncompletedDailies([item], today).Should().BeEmpty();
    }

    [Fact]
    public void GetYesterdayUncompletedDailies_StartBeforeYesterday_Listed_EvenWhenCreatedToday()
    {
        var today = new DateOnly(2026, 4, 27);
        var item = NewDaily("Backdated start", Utc(today), start: new DateOnly(2026, 4, 1));
        DailySchedule.GetYesterdayUncompletedDailies([item], today)
            .Select(x => x.Id).Should().Equal(item.Id);
    }

    private static DateTimeOffset Utc(DateOnly day) =>
        new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static BoardItem NewDaily(string title, DateTimeOffset createdUtc, DateOnly? start = null) => new(
        Guid.NewGuid(),
        title,
        IsCompleted: false,
        Counter: 0,
        DailyStartDate: start,
        DailyRepeat: DailyRepeatType.Daily,
        DailyRepeatInterval: 1,
        CreatedAtUtc: createdUtc);
}
