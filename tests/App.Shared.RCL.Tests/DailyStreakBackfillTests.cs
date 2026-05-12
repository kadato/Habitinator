using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using FluentAssertions;

namespace App.Shared.RCL.Tests;

public sealed class DailyStreakBackfillTests
{
    [Fact]
    public void GetLastN_DailyInterval1_Returns_N_Consecutive_Days_Ending_At_NotAfter()
    {
        var notAfter = new DateOnly(2026, 4, 27);
        var start = new DateOnly(2026, 4, 1);
        var days = DailyStreakBackfill.GetLastNScheduledCompletionDays(
            start, DailyRepeatType.Daily, 1, 3, notAfter);
        days.Should().HaveCount(3);
        days[0].Should().Be(notAfter);
        days[1].Should().Be(new DateOnly(2026, 4, 26));
        days[2].Should().Be(new DateOnly(2026, 4, 25));
    }

    [Fact]
    public void GetLastN_ManualStreak_UsesNotAfterYesterday_SoTodayIsNotIncluded()
    {
        // Ending the walk at "yesterday" (common when N counts previous scheduled days, not today).
        var notAfter = new DateOnly(2026, 4, 26);
        var start = new DateOnly(2026, 4, 1);
        var days = DailyStreakBackfill.GetLastNScheduledCompletionDays(
            start, DailyRepeatType.Daily, 1, 3, notAfter);
        days.Should().HaveCount(3);
        days[0].Should().Be(notAfter);
        days[1].Should().Be(new DateOnly(2026, 4, 25));
        days[2].Should().Be(new DateOnly(2026, 4, 24));
    }

    [Fact]
    public void GetLastN_When_DailyStart_Equals_NotAfter_Still_Returns_N_Prior_Scheduled_Days()
    {
        // When start date equals "yesterday" (notAfter), the streak-history floor must not be only that day, or
        // a manual streak of 3+ gets at most one backfill day and the board shows 1 after a same-day check.
        var notAfter = new DateOnly(2026, 4, 26);
        var startSameAsNotAfter = notAfter;
        var days = DailyStreakBackfill.GetLastNScheduledCompletionDays(
            startSameAsNotAfter, DailyRepeatType.Daily, 1, 3, notAfter);
        days.Should().HaveCount(3);
        days[0].Should().Be(notAfter);
        days[1].Should().Be(new DateOnly(2026, 4, 25));
        days[2].Should().Be(new DateOnly(2026, 4, 24));
    }

    [Fact]
    public void GetLastN_ZeroStreak_Returns_Empty()
    {
        var days = DailyStreakBackfill.GetLastNScheduledCompletionDays(
            new DateOnly(2026, 1, 1), DailyRepeatType.Daily, 1, 0, new DateOnly(2026, 4, 27));
        days.Should().BeEmpty();
    }

    [Fact]
    public void GetLastN_NullStart_Daily_Still_Backfills_Prior_Days()
    {
        // Without a stored start date, board "due" anchors at UTC today — streak history must not treat
        // yesterday as before the habit's schedule, or manual streak / stats heatmaps get no rows.
        var notAfter = new DateOnly(2026, 4, 26);
        var days = DailyStreakBackfill.GetLastNScheduledCompletionDays(
            null, DailyRepeatType.Daily, 1, 2, notAfter);
        days.Should().HaveCount(2);
        days[0].Should().Be(notAfter);
        days[1].Should().Be(new DateOnly(2026, 4, 25));
    }

    [Fact]
    public void GetLastN_StartAfterNotAfter_TreatedLikeMissingStart_ForBackfill()
    {
        // Matches "created today" rows that stored DailyStartDate = UTC today: yesterday must still backfill.
        var notAfter = new DateOnly(2026, 4, 26);
        var startTooLate = new DateOnly(2026, 4, 27);
        var days = DailyStreakBackfill.GetLastNScheduledCompletionDays(
            startTooLate, DailyRepeatType.Daily, 1, 2, notAfter);
        days.Should().HaveCount(2);
        days[0].Should().Be(notAfter);
        days[1].Should().Be(new DateOnly(2026, 4, 25));
    }

    [Fact]
    public void StreakBackfillTimestamp_Detects_Noon_Utc()
    {
        var ts = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        DailyStreakBackfill.IsStreakBackfillTimestamp(ts).Should().BeTrue();
        DailyStreakBackfill.IsStreakBackfillTimestamp(ts.AddSeconds(1)).Should().BeFalse();
        DailyStreakBackfill.IsStreakBackfillTimestamp(ts.AddHours(1)).Should().BeFalse();
    }
}
