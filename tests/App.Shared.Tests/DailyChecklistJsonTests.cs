using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class DailyChecklistJsonTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);

    [Fact]
    public void ResetDoneForCycle_ClearsChecksFromEarlierDailyCycles()
    {
        var json = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Yesterday", IsDone: true, DoneOn: Today.AddDays(-1)),
            new DailyChecklistItem(Guid.NewGuid(), "Today", IsDone: true, DoneOn: Today),
            new DailyChecklistItem(Guid.NewGuid(), "Open"),
        ])!;
        var daily = Daily(Today.AddDays(-10));

        var reset = DailyChecklistJson.ResetDoneForCycle(json, daily, Today)!;

        var rows = DailyChecklistJson.Parse(reset);
        rows.Single(x => x.Text == "Yesterday").IsDone.Should().BeFalse();
        rows.Single(x => x.Text == "Today").IsDone.Should().BeTrue();
        rows.Single(x => x.Text == "Open").IsDone.Should().BeFalse();
    }

    [Fact]
    public void ResetDoneForCycle_KeepsJsonWhenNothingIsStale()
    {
        var json = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Today", IsDone: true, DoneOn: Today),
        ])!;

        DailyChecklistJson.ResetDoneForCycle(json, Daily(Today.AddDays(-10)), Today).Should().BeSameAs(json);
    }

    [Fact]
    public void ResetDoneForCycle_WeeklyDailyKeepsChecksBetweenDueDays()
    {
        var weekStart = Today.AddDays(-1);
        var json = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Checked on the due day", IsDone: true, DoneOn: weekStart),
        ])!;
        var weekly = Daily(weekStart, DailyRepeatType.Weekly);

        var reset = DailyChecklistJson.ResetDoneForCycle(json, weekly, Today);

        DailyChecklistJson.Parse(reset).Should().ContainSingle().Which.IsDone.Should().BeTrue();
    }

    [Fact]
    public void ResetDoneForCycle_WeeklyDailyClearsChecksOnTheNextDueDay()
    {
        var previousDue = Today.AddDays(-7);
        var json = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Checked last week", IsDone: true, DoneOn: previousDue),
        ])!;
        var weekly = Daily(previousDue, DailyRepeatType.Weekly);

        var reset = DailyChecklistJson.ResetDoneForCycle(json, weekly, Today)!;

        DailyChecklistJson.Parse(reset).Should().ContainSingle().Which.IsDone.Should().BeFalse();
    }

    [Fact]
    public void ResetDoneForCycle_MonthlyDailyKeepsChecksUntilItsNextDueDay()
    {
        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        var today = monthStart.AddDays(10);
        var json = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Checked this month", IsDone: true, DoneOn: monthStart.AddDays(3)),
        ])!;
        var monthly = Daily(monthStart, DailyRepeatType.Monthly);

        var reset = DailyChecklistJson.ResetDoneForCycle(json, monthly, today);

        DailyChecklistJson.Parse(reset).Should().ContainSingle().Which.IsDone.Should().BeTrue();
    }

    [Fact]
    public void ResetDoneForCycle_MonthlyDailyClearsChecksFromThePreviousMonth()
    {
        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        var previousMonthStart = new DateOnly(monthStart.Year, monthStart.Month - 1, 1);
        var today = monthStart.AddDays(10);
        var json = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Checked last month", IsDone: true, DoneOn: previousMonthStart.AddDays(3)),
        ])!;
        var monthly = Daily(previousMonthStart, DailyRepeatType.Monthly);

        var reset = DailyChecklistJson.ResetDoneForCycle(json, monthly, today)!;

        DailyChecklistJson.Parse(reset).Should().ContainSingle().Which.IsDone.Should().BeFalse();
    }

    private static BoardItem Daily(DateOnly start, DailyRepeatType repeat = DailyRepeatType.Daily) =>
        new(
            Guid.NewGuid(),
            "Daily",
            DailyStartDate: start,
            DailyRepeat: repeat,
            DailyRepeatInterval: 1);

    [Fact]
    public void StampUndatedDone_UsesTheCurrentDay()
    {
        var json = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Checked now", IsDone: true),
            new DailyChecklistItem(Guid.NewGuid(), "Left open"),
        ])!;

        var stamped = DailyChecklistJson.StampUndatedDone(json, Today)!;

        var rows = DailyChecklistJson.Parse(stamped);
        rows.Single(x => x.Text == "Checked now").DoneOn.Should().Be(Today);
        rows.Single(x => x.Text == "Left open").DoneOn.Should().BeNull();
    }

    [Fact]
    public void Serialize_RoundTripsDoneOn()
    {
        var json = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Checked", IsDone: true, DoneOn: Today),
        ]);

        DailyChecklistJson.Parse(json).Should().ContainSingle().Which.DoneOn.Should().Be(Today);
    }

    [Fact]
    public void Serialize_DropsDoneOnForUncheckedRows()
    {
        var json = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Reopened", IsDone: false, DoneOn: Today),
        ]);

        DailyChecklistJson.Parse(json).Should().ContainSingle().Which.DoneOn.Should().BeNull();
    }

    [Fact]
    public void EditDialogRows_PreserveDoneOn()
    {
        var json = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Checked today", IsDone: true, DoneOn: Today),
        ]);

        var rows = EditDialogHelpers.ParseChecklistRows(json);
        rows.Should().ContainSingle().Which.DoneOn.Should().Be(Today);

        var roundTrip = EditDialogHelpers.SerializeChecklistRows(rows);
        DailyChecklistJson.Parse(roundTrip).Should().ContainSingle().Which.DoneOn.Should().Be(Today);
    }
}
