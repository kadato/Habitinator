using App.Shared.RCL.Models;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class UpcomingScheduleTests
{
    private static BoardItem NewDaily(
        string title,
        DateOnly? start,
        DailyRepeatType repeat,
        int interval,
        int weekdays = 0,
        DateOnly? lastCompleted = null,
        bool isCompleted = false) => new(
        Guid.NewGuid(),
        title,
        IsCompleted: isCompleted,
        DailyStartDate: start,
        DailyRepeat: repeat,
        DailyRepeatInterval: interval,
        DailyLastCompletedOn: lastCompleted,
        DailyWeekdays: weekdays);

    private static BoardItem NewTodo(string title, DateOnly? due, bool done = false) => new(
        Guid.NewGuid(),
        title,
        IsCompleted: done,
        TodoDueDate: due);

    [Fact]
    public void WalkForward_DailyInterval1_YieldsConsecutiveDays()
    {
        var days = DailySchedule.WalkScheduledDaysForward(
            new DateOnly(2024, 1, 1), DailyRepeatType.Daily, 1, new DateOnly(2024, 1, 1), 3).ToList();
        days.Should().Equal(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3));
    }

    [Fact]
    public void WalkForward_WeeklyMask_YieldsOnlySelectedDays()
    {
        var start = new DateOnly(2024, 1, 1); // Monday
        var mask = DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday);
        var days = DailySchedule.WalkScheduledDaysForward(
            start, DailyRepeatType.Weekly, 1, start, 8, mask).ToList();
        days.Should().Equal(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 3),
            new DateOnly(2024, 1, 5),
            new DateOnly(2024, 1, 8));
    }

    [Fact]
    public void WalkForward_ZeroOrNegativeWindow_YieldsNothing()
    {
        DailySchedule.WalkScheduledDaysForward(
            new DateOnly(2024, 1, 1), DailyRepeatType.Daily, 1, new DateOnly(2024, 1, 1), 0).Should().BeEmpty();
        DailySchedule.WalkScheduledDaysForward(
            new DateOnly(2024, 1, 1), DailyRepeatType.Daily, 1, new DateOnly(2024, 1, 1), -5).Should().BeEmpty();
    }

    [Fact]
    public void WalkForward_StopsAtMaxValue()
    {
        var days = DailySchedule.WalkScheduledDaysForward(
            null, DailyRepeatType.Daily, 1, DateOnly.MaxValue, 5).ToList();
        days.Should().Equal(DateOnly.MaxValue);
    }

    [Fact]
    public void GetUpcomingDays_TodayUsesDue_FutureUsesScheduled()
    {
        var today = new DateOnly(2024, 1, 8); // Monday
        var doneToday = NewDaily("Done", new DateOnly(2024, 1, 1), DailyRepeatType.Daily, 1, lastCompleted: today, isCompleted: true);
        var open = NewDaily("Open", new DateOnly(2024, 1, 1), DailyRepeatType.Daily, 1);
        var snapshot = new BoardSnapshot([], [doneToday, open], []);

        var days = UpcomingSchedule.GetUpcomingDays(snapshot, today, 2);

        days.Should().HaveCount(2);
        days[0].IsToday.Should().BeTrue();
        days[0].Dailies.Select(d => d.Title).Should().Equal("Open");
        days[1].IsToday.Should().BeFalse();
        days[1].Dailies.Select(d => d.Title).Should().BeEquivalentTo("Done", "Open");
    }

    [Fact]
    public void GetUpcomingDays_GroupsTodosByDueDate_AndListsOverdueSeparately()
    {
        var today = new DateOnly(2024, 1, 8);
        var overdue = NewTodo("Late", today.AddDays(-2));
        var dueToday = NewTodo("Today", today);
        var dueTomorrow = NewTodo("Tomorrow", today.AddDays(1));
        var done = NewTodo("Done", today, done: true);
        var undated = NewTodo("Someday", null);
        var snapshot = new BoardSnapshot([], [], [overdue, dueToday, dueTomorrow, done, undated]);

        var days = UpcomingSchedule.GetUpcomingDays(snapshot, today, 2);
        days[0].TodosDue.Select(t => t.Title).Should().Equal("Today");
        days[1].TodosDue.Select(t => t.Title).Should().Equal("Tomorrow");

        UpcomingSchedule.GetOverdueTodos(snapshot, today).Select(t => t.Title).Should().Equal("Late");
    }

    [Fact]
    public void GetUpcomingDays_ClampsWindow()
    {
        var snapshot = new BoardSnapshot([], [], []);
        UpcomingSchedule.GetUpcomingDays(snapshot, new DateOnly(2024, 1, 1), 0).Should().HaveCount(1);
        UpcomingSchedule.GetUpcomingDays(snapshot, new DateOnly(2024, 1, 1), 500).Should().HaveCount(UpcomingSchedule.MaxDaysAhead);
    }

    [Fact]
    public void DescribeDaily_CoversRepeatsAndMasks()
    {
        UpcomingSchedule.DescribeDaily(NewDaily("D", null, DailyRepeatType.Daily, 1)).Should().Be("Daily");
        UpcomingSchedule.DescribeDaily(NewDaily("D", null, DailyRepeatType.Daily, 3)).Should().Be("Every 3 days");
        UpcomingSchedule.DescribeDaily(NewDaily("D", null, DailyRepeatType.Weekly, 1)).Should().Be("Weekly");
        UpcomingSchedule.DescribeDaily(NewDaily("D", null, DailyRepeatType.Weekly, 2)).Should().Be("Every 2 weeks");
        UpcomingSchedule.DescribeDaily(NewDaily(
            "D", null, DailyRepeatType.Weekly, 1,
            DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday))).Should().Be("Mon, Wed, Fri");
        UpcomingSchedule.DescribeDaily(NewDaily(
            "D", null, DailyRepeatType.Weekly, 2,
            DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Wednesday))).Should().Be("Mon, Wed, every 2 weeks");
        UpcomingSchedule.DescribeDaily(NewDaily("D", null, DailyRepeatType.Monthly, 1)).Should().Be("Monthly");
        UpcomingSchedule.DescribeDaily(NewDaily("D", null, DailyRepeatType.Yearly, 2)).Should().Be("Every 2 years");
    }
}
