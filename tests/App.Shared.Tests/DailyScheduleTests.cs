using App.Shared.RCL.Models;
using App.Shared.Tests.TestDoubles;

using FluentAssertions;

namespace App.Shared.Tests;

public class DailyScheduleTests
{
    [Fact]
    public void WalkScheduledDaysBackward_StopsAtTheMinimumDate()
    {
        var days = DailySchedule.WalkScheduledDaysBackward(
            new DateOnly(1, 1, 3),
            DateOnly.MinValue,
            DailyRepeatType.Daily,
            1).ToList();

        days.Should().Equal(new DateOnly(1, 1, 3), new DateOnly(1, 1, 2), new DateOnly(1, 1, 1));
    }

    [Fact]
    public void YearlySchedule_WithAHugeStreakWindow_ReturnsTheMinimumDateAnchor()
    {
        var notAfter = new DateOnly(2026, 10, 1);
        var anchor = DailySchedule.StreakHistoryScheduleStart(
            notAfter,
            notAfter,
            DailyRepeatType.Yearly,
            1,
            9999);

        anchor.Should().Be(DateOnly.MinValue);
    }

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

    [Fact]
    public void Weekly_WithWeekdayMask_DueOnlyOnSelectedDays()
    {
        var start = new DateOnly(2024, 1, 1); // Monday
        var mask = DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday);

        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 1), mask).Should().BeTrue();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 2), mask).Should().BeFalse();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 3), mask).Should().BeTrue();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 5), mask).Should().BeTrue();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 6), mask).Should().BeFalse();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 7), mask).Should().BeFalse();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 8), mask).Should().BeTrue();
    }

    [Fact]
    public void Weekly_WithWeekdayMask_Interval2_SkipsAlternateWeeks()
    {
        var start = new DateOnly(2024, 1, 1); // Monday
        var mask = DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Wednesday);

        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 2, new DateOnly(2024, 1, 1), mask).Should().BeTrue();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 2, new DateOnly(2024, 1, 3), mask).Should().BeTrue();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 2, new DateOnly(2024, 1, 8), mask).Should().BeFalse();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 2, new DateOnly(2024, 1, 10), mask).Should().BeFalse();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 2, new DateOnly(2024, 1, 15), mask).Should().BeTrue();
    }

    [Fact]
    public void Weekly_ZeroMask_KeepsLegacySameDow()
    {
        var start = new DateOnly(2024, 1, 8); // Monday
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 15), 0).Should().BeTrue();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 9), 0).Should().BeFalse();
    }

    [Fact]
    public void Weekly_MaskDoesNotAffectOtherRepeats()
    {
        var start = new DateOnly(2024, 1, 1);
        var mask = DailyWeekdays.From(DayOfWeek.Monday);
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Daily, 1, new DateOnly(2024, 1, 2), mask).Should().BeTrue();
    }

    [Fact]
    public void Weekly_MaskOutOfRangeBits_AreIgnored()
    {
        var start = new DateOnly(2024, 1, 1); // Monday
        var mondayOnly = DailyWeekdays.From(DayOfWeek.Monday);
        var withHighBit = mondayOnly | (1 << 7) | (1 << 20);
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 1), withHighBit).Should().BeTrue();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 2), withHighBit).Should().BeFalse();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 1), mondayOnly).Should().Be(
            DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 1), withHighBit));
    }

    [Fact]
    public void Weekly_MaskStartDayNotInMask_StartDayIsNotDue()
    {
        var start = new DateOnly(2024, 1, 2); // Tuesday
        var mask = DailyWeekdays.From(DayOfWeek.Monday);
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, start, mask).Should().BeFalse();
        DailySchedule.IsScheduledOn(start, DailyRepeatType.Weekly, 1, new DateOnly(2024, 1, 8), mask).Should().BeTrue();
    }

    [Fact]
    public void CanCompleteForDate_RespectsWeekdayMask()
    {
        var start = new DateOnly(2024, 1, 1); // Monday
        var today = new DateOnly(2024, 1, 10); // Wednesday
        var mask = DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Wednesday);
        DailySchedule.CanCompleteForDate(start, DailyRepeatType.Weekly, 1, null, new DateOnly(2024, 1, 8), today, mask).Should().BeTrue();
        DailySchedule.CanCompleteForDate(start, DailyRepeatType.Weekly, 1, null, new DateOnly(2024, 1, 9), today, mask).Should().BeFalse();
    }

    [Fact]
    public void WalkBackward_WithMask_YieldsOnlySelectedDays()
    {
        var start = new DateOnly(2024, 1, 1); // Monday
        var mask = DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday);
        var days = DailySchedule.WalkScheduledDaysBackward(
            new DateOnly(2024, 1, 8),
            start,
            DailyRepeatType.Weekly,
            1,
            DailySchedule.MaxScheduledStepCap,
            mask).ToList();
        days.Should().Equal(new DateOnly(2024, 1, 8), new DateOnly(2024, 1, 5), new DateOnly(2024, 1, 3), new DateOnly(2024, 1, 1));
    }
}
