using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using FluentAssertions;

namespace App.Shared.RCL.Tests;

public sealed class ActivityStatisticsCalculatorTests
{
    private static readonly Guid DailyId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    private static readonly Guid TodoId = Guid.Parse("22222222-2222-2222-2222-222222222202");
    private static readonly Guid HabitId = Guid.Parse("33333333-3333-3333-3333-333333333303");

    private static readonly DateOnly Day = new(2026, 5, 21);

    private static readonly IReadOnlyDictionary<Guid, string> Titles = new Dictionary<Guid, string>
    {
        [DailyId] = "Morning routine",
        [TodoId] = "Buy milk",
        [HabitId] = "Meditate"
    };

    [Fact]
    public void BuildDayDetail_daily_complete_then_uncomplete_shows_nothing_for_item()
    {
        var rows = new[]
        {
            Row(Day, 9, ActivityEventType.DailyComplete, DailyId),
            Row(Day, 10, ActivityEventType.DailyUncomplete, DailyId)
        };

        var detail = ActivityStatisticsCalculator.BuildDayDetail(Day, rows, Titles);

        detail.Events.Should().BeEmpty();
        detail.FocusMinutesTotal.Should().Be(0);
        detail.Events.Should().NotContain(e => e.Label.Contains("undone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildDayDetail_daily_complete_uncomplete_complete_shows_single_final_complete()
    {
        var rows = new[]
        {
            Row(Day, 9, ActivityEventType.DailyComplete, DailyId),
            Row(Day, 10, ActivityEventType.DailyUncomplete, DailyId),
            Row(Day, 14, ActivityEventType.DailyComplete, DailyId)
        };

        var detail = ActivityStatisticsCalculator.BuildDayDetail(Day, rows, Titles);

        detail.Events.Should().ContainSingle();
        detail.Events[0].EventType.Should().Be(ActivityEventType.DailyComplete);
        detail.Events[0].OccurredAtUtc.Should().Be(At(Day, 14));
        detail.Events[0].BoardItemTitle.Should().Be("Morning routine");
        detail.Events[0].Label.Should().Be("Completed daily: Morning routine");
        detail.Events.Select(e => e.EventType).Should().NotContain(
            [ActivityEventType.DailyUncomplete, ActivityEventType.TodoUncomplete]);
    }

    [Fact]
    public void BuildDayDetail_todo_complete_then_uncomplete_shows_nothing_for_item()
    {
        var rows = new[]
        {
            Row(Day, 8, ActivityEventType.TodoComplete, TodoId),
            Row(Day, 11, ActivityEventType.TodoUncomplete, TodoId)
        };

        var detail = ActivityStatisticsCalculator.BuildDayDetail(Day, rows, Titles);

        detail.Events.Should().BeEmpty();
    }

    [Fact]
    public void BuildDayDetail_todo_complete_uncomplete_complete_shows_single_final_complete()
    {
        var rows = new[]
        {
            Row(Day, 8, ActivityEventType.TodoComplete, TodoId),
            Row(Day, 9, ActivityEventType.TodoUncomplete, TodoId),
            Row(Day, 15, ActivityEventType.TodoComplete, TodoId)
        };

        var detail = ActivityStatisticsCalculator.BuildDayDetail(Day, rows, Titles);

        detail.Events.Should().ContainSingle();
        detail.Events[0].EventType.Should().Be(ActivityEventType.TodoComplete);
        detail.Events[0].OccurredAtUtc.Should().Be(At(Day, 15));
    }

    [Fact]
    public void BuildDayDetail_mixed_day_keeps_habit_and_timer_when_daily_netted_out()
    {
        var rows = new[]
        {
            Row(Day, 7, ActivityEventType.DailyComplete, DailyId),
            Row(Day, 8, ActivityEventType.DailyUncomplete, DailyId),
            Row(Day, 9, ActivityEventType.HabitPlus, HabitId),
            Row(Day, 10, ActivityEventType.TimerSession, null, durationSeconds: 90)
        };

        var detail = ActivityStatisticsCalculator.BuildDayDetail(Day, rows, Titles);

        detail.Events.Should().HaveCount(2);
        detail.Events[0].EventType.Should().Be(ActivityEventType.HabitPlus);
        detail.Events[0].Label.Should().Be("Habit +: Meditate");
        detail.Events[1].EventType.Should().Be(ActivityEventType.TimerSession);
        detail.FocusMinutesTotal.Should().Be(2);
    }

    [Fact]
    public void BuildDashboard_todo_complete_then_uncomplete_counts_zero_for_day()
    {
        var rows = new[]
        {
            Row(Day, 8, ActivityEventType.TodoComplete, TodoId),
            Row(Day, 11, ActivityEventType.TodoUncomplete, TodoId)
        };

        var dashboard = BuildDashboardForDay(rows);

        DayCount(dashboard, Day).Should().Be(0);
    }

    [Fact]
    public void BuildDashboard_todo_complete_uncomplete_complete_counts_one_for_day()
    {
        var rows = new[]
        {
            Row(Day, 8, ActivityEventType.TodoComplete, TodoId),
            Row(Day, 9, ActivityEventType.TodoUncomplete, TodoId),
            Row(Day, 15, ActivityEventType.TodoComplete, TodoId)
        };

        var dashboard = BuildDashboardForDay(rows);

        DayCount(dashboard, Day).Should().Be(1);
    }

    [Fact]
    public void BuildDayDetail_uses_custom_label_when_board_title_missing()
    {
        var rows = new[] { Row(Day, 9, ActivityEventType.HabitPlus, HabitId, customLabel: "Meditate") };

        var detail = ActivityStatisticsCalculator.BuildDayDetail(Day, rows, new Dictionary<Guid, string>());

        detail.Events.Should().ContainSingle();
        detail.Events[0].Label.Should().Be("Habit +: Meditate");
    }

    [Fact]
    public void BuildDayDetail_sums_focus_minutes_from_aggregated_timer_rows()
    {
        var rows = new[]
        {
            Row(Day, 10, ActivityEventType.TimerSession, null, durationSeconds: 60),
            Row(Day, 12, ActivityEventType.TimerSession, null, durationSeconds: 90)
        };

        var detail = ActivityStatisticsCalculator.BuildDayDetail(Day, rows, Titles);

        detail.Events.Should().HaveCount(2);
        detail.FocusMinutesTotal.Should().Be(3);
    }

    private static ActivityDashboardDto BuildDashboardForDay(IReadOnlyList<UserActivityEventRecord> rows) =>
        ActivityStatisticsCalculator.BuildDashboard(
            rows,
            DailyGraphPeriods.Rolling370Days,
            Day,
            Day,
            Day);

    private static int DayCount(ActivityDashboardDto dashboard, DateOnly day) =>
        dashboard.Heatmap.First(c => c.Date == day && c.InDataRange).Count;

    private static UserActivityEventRecord Row(
        DateOnly day,
        int hourUtc,
        ActivityEventType type,
        Guid? boardItemId,
        int? durationSeconds = null,
        string? customLabel = null) =>
        new(At(day, hourUtc), type, boardItemId, durationSeconds, customLabel);

    [Fact]
    public void BuildDashboard_caps_range_and_grid_at_todayCutoff()
    {
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 12, 31);
        var todayCutoff = new DateOnly(2026, 5, 26);
        
        var dashboard = ActivityStatisticsCalculator.BuildDashboard(
            [],
            DailyGraphPeriods.ForCalendarYear(2026),
            start,
            end,
            todayCutoff);

        dashboard.RangeEnd.Should().Be(todayCutoff);
        dashboard.GridWeekColumns.Should().BeLessThan(53);
    }

    [Fact]
    public void BuildDailyContributions_resizes_based_on_earliest_daily_start_date()
    {
        var rangeStart = new DateOnly(2026, 1, 1);
        var rangeEnd = new DateOnly(2026, 12, 31);
        var todayCutoff = new DateOnly(2026, 5, 26);

        var dailies = new[]
        {
            new DailyItemStatsDto(Guid.NewGuid(), "Daily A", new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 8)),
            new DailyItemStatsDto(Guid.NewGuid(), "Daily B", null, new DateOnly(2026, 5, 15))
        };

        var view = ActivityStatisticsCalculator.BuildDailyContributions(
            [],
            dailies,
            DailyGraphPeriods.ForCalendarYear(2026),
            [],
            rangeStart,
            rangeEnd,
            todayCutoff);

        view.RangeStart.Should().Be(new DateOnly(2026, 5, 10));
        view.RangeEnd.Should().Be(todayCutoff);
    }

    private static DateTimeOffset At(DateOnly day, int hourUtc) =>
        new(day.Year, day.Month, day.Day, hourUtc, 0, 0, TimeSpan.Zero);
}
