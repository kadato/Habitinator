using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

namespace App.Shared.Tests;

public class DailyReminderTextTests
{
    [Fact]
    public void Build_IncludesDailiesDue_Today()
    {
        var today = new DateOnly(2026, 4, 1);
        var d = new BoardItem(
            Guid.NewGuid(),
            "Morning run",
            false,
            0,
            null,
            null,
            true,
            true,
            0,
            HabitResetPeriod.Daily,
            today,
            DailyRepeatType.Daily,
            1,
            null,
            null,
            null);
        var snapshot = new BoardSnapshot([], [d], []);
        (string t, string body) = DailyReminderText.Build(snapshot, today, maxBodyLength: 4000);
        Assert.Equal(DailyReminderText.DefaultTitle, t);
        Assert.Contains("Dailies due:", body);
        Assert.Contains("Morning run", body);
    }

    [Fact]
    public void Build_IncludesTodo_WithDeadline_TodayOrOverdue()
    {
        var today = new DateOnly(2026, 4, 10);
        var todoDueToday = new BoardItem(Guid.NewGuid(), "Hand in form", false, 0, null, null, true, true, 0, HabitResetPeriod.Daily, null, DailyRepeatType.Daily, 1, null, null, today);
        var snapshot = new BoardSnapshot([], [], [todoDueToday]);
        (string _, string body) = DailyReminderText.Build(snapshot, today);
        Assert.Contains("To-dos (deadline):", body);
        Assert.Contains("Hand in form", body);
    }

    [Fact]
    public void Build_IncludesOverdue_Todo_Marked()
    {
        var today = new DateOnly(2026, 4, 10);
        var oldDue = new DateOnly(2026, 4, 1);
        var t = new BoardItem(Guid.NewGuid(), "Late", false, 0, null, null, true, true, 0, HabitResetPeriod.Daily, null, DailyRepeatType.Daily, 1, null, null, oldDue);
        var snapshot = new BoardSnapshot([], [], [t]);
        (string _, string body) = DailyReminderText.Build(snapshot, today, "dd/MM/yyyy");
        Assert.Contains("overdue", body, StringComparison.Ordinal);
        Assert.Contains("01/04/2026", body);
    }

    [Fact]
    public void Build_Empty_When_Nothing_Due()
    {
        var today = new DateOnly(2026, 4, 1);
        var done = new BoardItem(
            Guid.NewGuid(),
            "Done",
            true,
            0,
            null,
            null,
            true,
            true,
            0,
            HabitResetPeriod.Daily,
            today,
            DailyRepeatType.Daily,
            1,
            null,
            today,
            null);
        var snapshot = new BoardSnapshot([], [done], [new BoardItem(Guid.NewGuid(), "No date", false)]);
        (string t, string body) = DailyReminderText.Build(snapshot, today);
        Assert.Equal(DailyReminderText.DefaultTitle, t);
        Assert.Contains("no dailies due", body, StringComparison.OrdinalIgnoreCase);
    }
}
