#pragma warning disable MUD0012

using App.Shared.RCL.Components;
using App.Shared.RCL.Services;

using Bunit;
using Bunit.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class StatisticsPanelTests : IAsyncDisposable
{
    private const string OverviewStateKey = "stats_overview_data";

    private readonly BunitContext _ctx = new();
    private readonly IActivityStatisticsReader _stats = Substitute.For<IActivityStatisticsReader>();
    private readonly IBoardDataService _boardData = Substitute.For<IBoardDataService>();
    private readonly IUserDateFormatService _dateFormat = Substitute.For<IUserDateFormatService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IBoardSyncStatus _syncStatus = Substitute.For<IBoardSyncStatus>();
    private readonly BunitPersistentComponentState _persistentState;

    public StatisticsPanelTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_stats);
        _ctx.Services.AddSingleton(_boardData);
        _ctx.Services.AddSingleton(_dateFormat);
        _ctx.Services.AddSingleton(_notifier);
        _ctx.Services.AddSingleton(_dialogs);
        _ctx.Services.AddSingleton(_syncStatus);
        _ctx.Services.AddSingleton<TestUserDayService>();
        _ctx.Services.AddSingleton<IUserDayService>(sp => sp.GetRequiredService<TestUserDayService>());
        _persistentState = _ctx.AddBunitPersistentComponentState();
        _ctx.Render<MudPopoverProvider>();

        _boardData.GetStreakMapAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Dictionary<Guid, int>>([]));
        _dateFormat.Format(Arg.Any<DateOnly>())
            .Returns(x => ((DateOnly)x[0]!).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Loads_the_whole_dashboard_with_one_overview_call()
    {
        var overview = BuildOverview();
        _stats.GetOverviewAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(overview));

        var cut = _ctx.Render<StatisticsPanel>();

        await cut.WaitForAssertionAsync(
            () => _ = _stats.Received(1).GetOverviewAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()),
            TimeSpan.FromSeconds(5));

        _ = _stats.DidNotReceive().GetDashboardAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        _ = _stats.DidNotReceive().GetDailyContributionsAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        _ = _stats.DidNotReceive().GetHabitContributionsAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());

        cut.Markup.Should().Contain("Activity heatmap");
    }

    [Fact]
    public async Task Revalidates_after_painting_the_persisted_overview()
    {
        var overview = BuildOverview();
        _stats.GetOverviewAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(overview));

        _persistentState.Persist(OverviewStateKey, overview);

        var cut = _ctx.Render<StatisticsPanel>();

        await cut.WaitForAssertionAsync(
            () => _ = _stats.Received(1).GetOverviewAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()),
            TimeSpan.FromSeconds(5));

        cut.Markup.Should().Contain("Activity heatmap");
    }

    private static ActivityOverviewDto BuildOverview()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dashboard = new ActivityDashboardDto(
            DailyGraphPeriods.Rolling370Days,
            WeekBars: [],
            Heatmap: [],
            GridWeekColumns: 1,
            TotalEvents: 0,
            TotalFocusMinutes: 0,
            MaxDayCount: 0,
            BusiestDay: null,
            RangeStart: today.AddDays(-1),
            RangeEnd: today,
            HeatmapDataDayCount: 1,
            WeekBarsRangeStart: today,
            WeekBarsRangeEnd: today,
            AvailableTags: []);
        var daily = new DailyContributionsViewDto(
            DailyGraphPeriods.Rolling370Days,
            [new DailyGraphPeriodOption(DailyGraphPeriods.Rolling370Days, "Last 370 days")],
            Graphs: [],
            RangeStart: today.AddDays(-1),
            RangeEnd: today);
        var habit = new HabitContributionsViewDto(
            DailyGraphPeriods.Rolling370Days,
            [new DailyGraphPeriodOption(DailyGraphPeriods.Rolling370Days, "Last 370 days")],
            Graphs: [],
            RangeStart: today.AddDays(-1),
            RangeEnd: today);
        return new ActivityOverviewDto(dashboard, daily, habit);
    }
}
