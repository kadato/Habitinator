using App.Shared.RCL.Models;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class BoardQuickFilterRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 26);

    [Fact]
    public void DueNow_keeps_today_daily_and_open_todos_due_today_or_earlier()
    {
        var daily = new BoardItem(Guid.NewGuid(), "Workout", DailyStartDate: Today);
        var overdueTodo = new BoardItem(Guid.NewGuid(), "Return books", TodoDueDate: Today.AddDays(-2));
        var todayTodo = new BoardItem(Guid.NewGuid(), "Buy milk", TodoDueDate: Today);
        var futureTodo = new BoardItem(Guid.NewGuid(), "Plan trip", TodoDueDate: Today.AddDays(2));

        BoardQuickFilterRules.Matches(BoardSection.Daily, daily, BoardQuickFilter.DueNow, Today).Should().BeTrue();
        BoardQuickFilterRules.Matches(BoardSection.Todo, overdueTodo, BoardQuickFilter.DueNow, Today).Should().BeTrue();
        BoardQuickFilterRules.Matches(BoardSection.Todo, todayTodo, BoardQuickFilter.DueNow, Today).Should().BeTrue();
        BoardQuickFilterRules.Matches(BoardSection.Todo, futureTodo, BoardQuickFilter.DueNow, Today).Should().BeFalse();
    }

    [Fact]
    public void DueNow_keeps_completed_todos_out()
    {
        var done = new BoardItem(Guid.NewGuid(), "Done", IsCompleted: true, TodoDueDate: Today);

        BoardQuickFilterRules.Matches(BoardSection.Todo, done, BoardQuickFilter.DueNow, Today).Should().BeFalse();
    }

    [Fact]
    public void Overdue_hides_habits_and_keeps_only_past_open_todos()
    {
        var habit = new BoardItem(Guid.NewGuid(), "Meditate");
        var overdue = new BoardItem(Guid.NewGuid(), "Return books", TodoDueDate: Today.AddDays(-1));
        var today = new BoardItem(Guid.NewGuid(), "Buy milk", TodoDueDate: Today);

        BoardQuickFilterRules.Matches(BoardSection.Habit, habit, BoardQuickFilter.Overdue, Today).Should().BeFalse();
        BoardQuickFilterRules.Matches(BoardSection.Todo, overdue, BoardQuickFilter.Overdue, Today).Should().BeTrue();
        BoardQuickFilterRules.Matches(BoardSection.Todo, today, BoardQuickFilter.Overdue, Today).Should().BeFalse();
    }

    [Fact]
    public void ThisWeek_keeps_items_due_inside_seven_days_and_drops_later_ones()
    {
        var inside = new BoardItem(Guid.NewGuid(), "Inside", TodoDueDate: Today.AddDays(3));
        var lastDay = new BoardItem(Guid.NewGuid(), "Last day", TodoDueDate: Today.AddDays(6));
        var outside = new BoardItem(Guid.NewGuid(), "Outside", TodoDueDate: Today.AddDays(7));

        BoardQuickFilterRules.Matches(BoardSection.Todo, inside, BoardQuickFilter.ThisWeek, Today).Should().BeTrue();
        BoardQuickFilterRules.Matches(BoardSection.Todo, lastDay, BoardQuickFilter.ThisWeek, Today).Should().BeTrue();
        BoardQuickFilterRules.Matches(BoardSection.Todo, outside, BoardQuickFilter.ThisWeek, Today).Should().BeFalse();
    }

    [Fact]
    public void ThisWeek_keeps_a_weekly_daily_scheduled_inside_the_window()
    {
        var scheduledDay = Today.AddDays(3);
        var daily = new BoardItem(
            Guid.NewGuid(),
            "Water plants",
            DailyStartDate: Today,
            DailyRepeat: DailyRepeatType.Weekly,
            DailyWeekdays: DailyWeekdays.From(scheduledDay.DayOfWeek));

        BoardQuickFilterRules.Matches(BoardSection.Daily, daily, BoardQuickFilter.ThisWeek, Today).Should().BeTrue();
    }

    [Fact]
    public void All_keeps_everything()
    {
        var future = new BoardItem(Guid.NewGuid(), "Later", TodoDueDate: Today.AddDays(30));

        BoardQuickFilterRules.Matches(BoardSection.Todo, future, BoardQuickFilter.All, Today).Should().BeTrue();
    }
}
