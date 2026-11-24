using App.Shared.RCL.Models;

using FluentAssertions;

namespace App.Shared.Tests;

public class BoardItemMapperTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public void Daily_CompletedToday_StaysCompleted()
    {
        const BoardSection section = BoardSection.Daily;
        var item = new BoardItem(Guid.NewGuid(), "Run", DailyLastCompletedOn: Today);

        BoardItemMapper.WithLocalDay(item, section, Today).IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void Daily_CompletedYesterday_IsNotCompleted()
    {
        const BoardSection section = BoardSection.Daily;
        var item = new BoardItem(Guid.NewGuid(), "Run", DailyLastCompletedOn: Today.AddDays(-1));

        BoardItemMapper.WithLocalDay(item, section, Today).IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void Daily_LegacyCompletedWithoutDate_StaysCompleted()
    {
        const BoardSection section = BoardSection.Daily;
        var item = new BoardItem(Guid.NewGuid(), "Run", IsCompleted: true);

        BoardItemMapper.WithLocalDay(item, section, Today).IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void Daily_SubtaskCheckedToday_StaysChecked()
    {
        const BoardSection section = BoardSection.Daily;
        var checklist = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Warm up", IsDone: true, DoneOn: Today),
            new DailyChecklistItem(Guid.NewGuid(), "Run"),
        ]);
        var item = new BoardItem(Guid.NewGuid(), "Run", ChecklistJson: checklist);

        var mapped = BoardItemMapper.WithLocalDay(item, section, Today);

        var rows = DailyChecklistJson.Parse(mapped.ChecklistJson);
        rows.Should().ContainSingle(x => x.Text == "Warm up").Which.IsDone.Should().BeTrue();
    }

    [Fact]
    public void Daily_SubtaskCheckedOnEarlierDay_IsNotChecked()
    {
        const BoardSection section = BoardSection.Daily;
        var checklist = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Warm up", IsDone: true, DoneOn: Today.AddDays(-1)),
        ]);
        var item = new BoardItem(Guid.NewGuid(), "Run", ChecklistJson: checklist);

        var mapped = BoardItemMapper.WithLocalDay(item, section, Today);

        var rows = DailyChecklistJson.Parse(mapped.ChecklistJson);
        rows.Should().ContainSingle(x => x.Text == "Warm up").Which.IsDone.Should().BeFalse();
    }

    [Fact]
    public void Daily_LegacySubtaskCheckedWithoutDay_IsNotChecked()
    {
        const BoardSection section = BoardSection.Daily;
        var checklist = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Warm up", IsDone: true),
        ]);
        var item = new BoardItem(Guid.NewGuid(), "Run", ChecklistJson: checklist);

        var mapped = BoardItemMapper.WithLocalDay(item, section, Today);

        DailyChecklistJson.Parse(mapped.ChecklistJson).Should().ContainSingle().Which.IsDone.Should().BeFalse();
    }

    [Fact]
    public void WeeklyDaily_SubtaskCheckedOnDueDay_StaysCheckedMidWeek()
    {
        const BoardSection section = BoardSection.Daily;
        var dueDay = Today.AddDays(-1);
        var checklist = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Warm up", IsDone: true, DoneOn: dueDay),
        ])!;
        var item = new BoardItem(
            Guid.NewGuid(),
            "Weekly review",
            ChecklistJson: checklist,
            DailyStartDate: dueDay,
            DailyRepeat: DailyRepeatType.Weekly);

        var mapped = BoardItemMapper.WithLocalDay(item, section, Today);

        DailyChecklistJson.Parse(mapped.ChecklistJson).Should().ContainSingle().Which.IsDone.Should().BeTrue();
    }

    [Fact]
    public void WeeklyDaily_SubtaskCheckedLastCycle_ClearsOnNextDueDay()
    {
        const BoardSection section = BoardSection.Daily;
        var previousDue = Today.AddDays(-7);
        var checklist = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Warm up", IsDone: true, DoneOn: previousDue),
        ])!;
        var item = new BoardItem(
            Guid.NewGuid(),
            "Weekly review",
            ChecklistJson: checklist,
            DailyStartDate: previousDue,
            DailyRepeat: DailyRepeatType.Weekly);

        var mapped = BoardItemMapper.WithLocalDay(item, section, Today);

        DailyChecklistJson.Parse(mapped.ChecklistJson).Should().ContainSingle().Which.IsDone.Should().BeFalse();
    }

    [Fact]
    public void Todo_SubtaskCheckedOnEarlierDay_StaysChecked()
    {
        const BoardSection section = BoardSection.Todo;
        var checklist = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Buy milk", IsDone: true, DoneOn: Today.AddDays(-1)),
        ]);
        var item = new BoardItem(Guid.NewGuid(), "Groceries", ChecklistJson: checklist);

        var mapped = BoardItemMapper.WithLocalDay(item, section, Today);

        DailyChecklistJson.Parse(mapped.ChecklistJson).Should().ContainSingle().Which.IsDone.Should().BeTrue();
    }

    [Fact]
    public void Habit_StalePeriod_ShowsZeroAndCurrentAnchor()
    {
        const BoardSection section = BoardSection.Habit;
        var item = new BoardItem(
            Guid.NewGuid(), "Read", Counter: 5, NegativeCounter: 2,
            ResetPeriod: HabitResetPeriod.Daily, HabitPeriodStart: Today.AddDays(-1));

        var mapped = BoardItemMapper.WithLocalDay(item, section, Today);

        mapped.Counter.Should().Be(0);
        mapped.NegativeCounter.Should().Be(0);
        mapped.HabitPeriodStart.Should().Be(Today);
    }

    [Fact]
    public void Habit_CurrentPeriod_KeepsCounters()
    {
        const BoardSection section = BoardSection.Habit;
        var item = new BoardItem(
            Guid.NewGuid(), "Read", Counter: 5, NegativeCounter: 2,
            ResetPeriod: HabitResetPeriod.Daily, HabitPeriodStart: Today);

        var mapped = BoardItemMapper.WithLocalDay(item, section, Today);

        mapped.Counter.Should().Be(5);
        mapped.NegativeCounter.Should().Be(2);
    }

    [Fact]
    public void Habit_LegacyNullAnchor_PreservesCounters()
    {
        const BoardSection section = BoardSection.Habit;
        var item = new BoardItem(Guid.NewGuid(), "Read", Counter: 5, ResetPeriod: HabitResetPeriod.Weekly);

        var mapped = BoardItemMapper.WithLocalDay(item, section, Today);

        mapped.Counter.Should().Be(5);
        mapped.HabitPeriodStart.Should().Be(HabitResetSchedule.PeriodStartFor(Today, HabitResetPeriod.Weekly));
    }

    [Fact]
    public void Todo_PassesThroughUnchanged()
    {
        const BoardSection section = BoardSection.Todo;
        var item = new BoardItem(Guid.NewGuid(), "Milk", IsCompleted: true, Counter: 3);

        BoardItemMapper.WithLocalDay(item, section, Today).Should().Be(item);
    }
}
