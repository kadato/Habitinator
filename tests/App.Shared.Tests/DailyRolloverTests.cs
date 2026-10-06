using App.Shared.RCL.Models;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class DailyRolloverTests
{
    private static readonly DateOnly s_today = new(2026, 8, 12); // Wednesday

    [Fact]
    public void GetOverdueSince_WeeklyMissedMonday_ReportsMondayOnWednesday()
    {
        var monday = new DateOnly(2026, 8, 10);
        var item = NewDaily("Gym", Utc(monday.AddDays(-14)), start: monday) with
        {
            DailyRepeat = DailyRepeatType.Weekly
        };

        DailyRollover.GetOverdueSince([item], s_today).Should().ContainKey(item.Id).WhoseValue.Should().Be(monday);
    }

    [Fact]
    public void GetOverdueSince_MissedOnlyYesterdayAndDueToday_StaysClean()
    {
        var item = NewDaily("Vitamins", Utc(s_today.AddDays(-10)));

        DailyRollover.GetOverdueSince([item], s_today, maxDaysBack: 1).Should().BeEmpty();
    }

    [Fact]
    public void GetOverdueSince_MissedYesterdayButNotScheduledToday_IsOverdue()
    {
        var monday = new DateOnly(2026, 8, 10);
        var tuesday = new DateOnly(2026, 8, 11);
        var item = NewDaily("Gym", Utc(monday.AddDays(-14)), start: monday) with
        {
            DailyRepeat = DailyRepeatType.Weekly
        };

        DailyRollover.GetOverdueSince([item], tuesday, maxDaysBack: 1)
            .Should().ContainKey(item.Id).WhoseValue.Should().Be(monday);
    }

    [Fact]
    public void GetOverdueSince_CheckedToday_IsNotOverdue()
    {
        var item = NewDaily("Done today", Utc(s_today.AddDays(-10))) with
        {
            DailyLastCompletedOn = s_today
        };

        DailyRollover.GetOverdueSince([item], s_today).Should().BeEmpty();
    }

    [Fact]
    public void GetOverdueSince_NeverCompleted_ReportsWindowStartNotOlder()
    {
        var item = NewDaily("Old", Utc(s_today.AddDays(-30)));

        var overdue = DailyRollover.GetOverdueSince([item], s_today);
        overdue.Should().ContainKey(item.Id).WhoseValue.Should().Be(s_today.AddDays(-7));
    }

    [Fact]
    public void GetOverdueSince_CompletedYesterday_ReportsOldestMissNotYesterday()
    {
        var item = NewDaily("Stale", Utc(s_today.AddDays(-10))) with
        {
            DailyLastCompletedOn = s_today.AddDays(-1)
        };

        DailyRollover.GetOverdueSince([item], s_today, maxDaysBack: 3)
            .Should().ContainKey(item.Id).WhoseValue.Should().Be(s_today.AddDays(-3));
    }

    [Fact]
    public void GetOverdueSince_NewDailyCreatedToday_IsNotOverdue()
    {
        var item = NewDaily("Created today", Utc(s_today));

        DailyRollover.GetOverdueSince([item], s_today).Should().BeEmpty();
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
