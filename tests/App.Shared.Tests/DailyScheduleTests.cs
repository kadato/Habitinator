using App.Shared.RCL.Models;
using App.Shared.Tests.TestDoubles;

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

    [Fact]
    public void LocalDay_WithoutTimeZone_UsesUtcDay()
    {
        var instant = new DateTimeOffset(2026, 4, 27, 1, 0, 0, TimeSpan.Zero);
        DailySchedule.LocalDay(instant).Should().Be(new DateOnly(2026, 4, 27));
    }

    [Fact]
    public void LocalDay_EastOfUtc_RollsEarlierUtcInstantToLocalDay()
    {
        var tz = new FixedOffsetTimeZoneService(TimeSpan.FromHours(2));
        var instant = new DateTimeOffset(2026, 4, 26, 22, 30, 0, TimeSpan.Zero);
        DailySchedule.LocalDay(instant, tz).Should().Be(new DateOnly(2026, 4, 27));
    }

    [Fact]
    public void LocalDay_WestOfUtc_RollsLaterUtcInstantBackToLocalDay()
    {
        var tz = new FixedOffsetTimeZoneService(TimeSpan.FromHours(-8));
        var instant = new DateTimeOffset(2026, 4, 27, 7, 0, 0, TimeSpan.Zero);
        DailySchedule.LocalDay(instant, tz).Should().Be(new DateOnly(2026, 4, 26));
    }

    [Fact]
    public void LocalDay_AppliesDayStartRollback_MatchingLocalToday()
    {
        var dayStart = TimeSpan.FromHours(5);
        var instant = new DateTimeOffset(2026, 4, 27, 0, 30, 0, TimeSpan.Zero);
        DailySchedule.LocalDay(instant, dayStartLocalTime: dayStart).Should().Be(new DateOnly(2026, 4, 26));
        DailySchedule.LocalDay(instant).Should().Be(new DateOnly(2026, 4, 27));
    }

    [Fact]
    public void IsCompletedForToday_HonorsDateAndLegacyState()
    {
        var today = new DateOnly(2026, 4, 27);
        DailySchedule.IsCompletedForToday(today, true, today).Should().BeTrue();
        DailySchedule.IsCompletedForToday(today.AddDays(-1), true, today).Should().BeFalse();
        DailySchedule.IsCompletedForToday(null, true, today).Should().BeTrue();
        DailySchedule.IsCompletedForToday(null, false, today).Should().BeFalse();
    }

    [Fact]
    public void ToggleForToday_ChecksTodayAndUnchecksYesterday()
    {
        var today = new DateOnly(2026, 4, 27);

        DailySchedule.ToggleForToday(null, false, today).Should().Be((today, true));
        DailySchedule.ToggleForToday(today, true, today).Should().Be((null, false));
        DailySchedule.ToggleForToday(null, true, today).Should().Be((null, false));
        DailySchedule.ToggleForToday(today.AddDays(-1), true, today).Should().Be((today, true));
    }

    [Fact]
    public void CanCompleteForDate_MatchesServerGuards()
    {
        var start = new DateOnly(2026, 4, 1);
        var today = new DateOnly(2026, 4, 27);
        var yesterday = today.AddDays(-1);

        DailySchedule.CanCompleteForDate(start, DailyRepeatType.Daily, 1, null, yesterday, today).Should().BeTrue();
        // Not a past date.
        DailySchedule.CanCompleteForDate(start, DailyRepeatType.Daily, 1, null, today, today).Should().BeFalse();
        // Already checked today.
        DailySchedule.CanCompleteForDate(start, DailyRepeatType.Daily, 1, today, yesterday, today).Should().BeFalse();
        // Already completed for the target day.
        DailySchedule.CanCompleteForDate(start, DailyRepeatType.Daily, 1, yesterday, yesterday, today).Should().BeFalse();
        // Day not scheduled for the item.
        DailySchedule.CanCompleteForDate(start, DailyRepeatType.Weekly, 1, null, today.AddDays(-2), today).Should().BeFalse();
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
