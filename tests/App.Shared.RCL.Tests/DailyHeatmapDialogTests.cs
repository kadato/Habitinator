#pragma warning disable MUD0012

using System.Globalization;

using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class DailyHeatmapDialogTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IActivityStatisticsReader _stats = Substitute.For<IActivityStatisticsReader>();
    private readonly IUserTimeZoneService _timeZoneService = Substitute.For<IUserTimeZoneService>();
    private readonly IUserDateFormatService _dateFormatService = Substitute.For<IUserDateFormatService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();

    public DailyHeatmapDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_stats);
        _ctx.Services.AddSingleton(_timeZoneService);
        _ctx.Services.AddSingleton(_dateFormatService);
        _ctx.Services.AddSingleton<IUserNotifier>(_notifier);

        _timeZoneService.LocalToday.Returns(new DateOnly(2026, 8, 12));
        _dateFormatService.DateFormat.Returns("yyyy-MM-dd");
        _dateFormatService.Format(Arg.Any<DateOnly>()).Returns(x => ((DateOnly)x[0]!).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Renders_HeatmapDialog_With_Daily_Graph_Data()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var today = new DateOnly(2026, 8, 12);
        var cells = new List<ActivityHeatmapCellDto>
        {
            new(0, 0, today, 1, 1, true)
        };
        var graphs = new List<DailyContributionGraphDto>
        {
            new(itemId, "Morning Run", cells, 52, 1, ["r370"])
        };
        var periodOptions = new List<DailyGraphPeriodOption>
        {
            new("r370", "Last 370 days")
        };
        var dto = new DailyContributionsViewDto("r370", periodOptions, graphs, today.AddDays(-30), today);
        _stats.GetDailyContributionsAsync("r370", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(dto));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<DailyHeatmapDialog>
        {
            { x => x.BoardItemId, itemId },
            { x => x.Title, "Morning Run" }
        };

        // Act
        await provider.InvokeAsync(async () => await dialogService.ShowAsync<DailyHeatmapDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("1 of 1"), TimeSpan.FromSeconds(5));

        // Assert
        provider.Markup.Should().Contain("Morning Run");
        provider.Markup.Should().Contain("1 of 1");
        provider.Markup.Should().Contain("stats-cell");
        provider.Markup.Should().NotContain("daily-heatmap-shell");
    }

    [Fact]
    public async Task Hides_Period_Options_Without_Recorded_Data()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var today = new DateOnly(2026, 8, 12);
        var cells = new List<ActivityHeatmapCellDto>
        {
            new(0, 0, today, 1, 1, true)
        };
        var graphs = new List<DailyContributionGraphDto>
        {
            new(itemId, "Morning Run", cells, 52, 1, ["r370", "y2026"])
        };
        var periodOptions = new List<DailyGraphPeriodOption>
        {
            new("r370", "Last 370 days"),
            new("y2026", "2026"),
            new("y2025", "2025")
        };
        var dto = new DailyContributionsViewDto("r370", periodOptions, graphs, today.AddDays(-30), today);
        _stats.GetDailyContributionsAsync("r370", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(dto));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<DailyHeatmapDialog>
        {
            { x => x.BoardItemId, itemId },
            { x => x.Title, "Morning Run" }
        };

        // Act
        await provider.InvokeAsync(async () => await dialogService.ShowAsync<DailyHeatmapDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("2026"), TimeSpan.FromSeconds(5));

        // Assert
        provider.Markup.Should().Contain("2026");
        provider.Markup.Should().NotContain("2025");
    }

    [Fact]
    public async Task Marks_due_open_days_as_missed()
    {
        var itemId = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 11);
        var cells = new List<ActivityHeatmapCellDto>
        {
            new(0, 0, date, 0, 0, true, Due: true)
        };
        var graphs = new List<DailyContributionGraphDto>
        {
            new(itemId, "Morning Run", cells, 52, 0, ["r370"])
        };
        var dto = new DailyContributionsViewDto("r370", [new("r370", "Last 370 days")], graphs, date.AddDays(-30), date);
        _stats.GetDailyContributionsAsync("r370", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(dto));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<DailyHeatmapDialog>
        {
            { x => x.BoardItemId, itemId },
            { x => x.Title, "Morning Run" }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<DailyHeatmapDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("stats-daily-due"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().Contain("Due, not done");
        provider.Markup.Should().Contain("daily-heatmap-legend");
    }

    [Fact]
    public async Task Leaves_non_due_days_plain()
    {
        var itemId = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 11);
        var cells = new List<ActivityHeatmapCellDto>
        {
            new(0, 0, date, 0, 0, true, Due: false)
        };
        var graphs = new List<DailyContributionGraphDto>
        {
            new(itemId, "Monthly review", cells, 52, 0, ["r370"])
        };
        var dto = new DailyContributionsViewDto("r370", [new("r370", "Last 370 days")], graphs, date.AddDays(-30), date);
        _stats.GetDailyContributionsAsync("r370", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(dto));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<DailyHeatmapDialog>
        {
            { x => x.BoardItemId, itemId },
            { x => x.Title, "Monthly review" }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<DailyHeatmapDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Monthly review"), TimeSpan.FromSeconds(5));
        provider.Render();

        provider.Markup.Should().NotContain("stats-daily-due");
        provider.Markup.Should().Contain("Not due");
    }

    [Fact]
    public async Task Marks_completed_due_days()
    {
        var itemId = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 11);
        var cells = new List<ActivityHeatmapCellDto>
        {
            new(0, 0, date, 1, 2, true, Due: true)
        };
        var graphs = new List<DailyContributionGraphDto>
        {
            new(itemId, "Morning Run", cells, 52, 1, ["r370"])
        };
        var dto = new DailyContributionsViewDto("r370", [new("r370", "Last 370 days")], graphs, date.AddDays(-30), date);
        _stats.GetDailyContributionsAsync("r370", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(dto));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<DailyHeatmapDialog>
        {
            { x => x.BoardItemId, itemId },
            { x => x.Title, "Morning Run" }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<DailyHeatmapDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("stats-daily-due"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().Contain("Done");
    }
}
