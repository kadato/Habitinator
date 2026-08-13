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

    public DailyHeatmapDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_stats);
        _ctx.Services.AddSingleton(_timeZoneService);
        _ctx.Services.AddSingleton(_dateFormatService);

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
        await provider.WaitForStateAsync(() => provider.Markup.Contains("1 completed"), TimeSpan.FromSeconds(5));

        // Assert
        provider.Markup.Should().Contain("Morning Run · 1 completed");
        provider.Markup.Should().Contain("stats-cell");
        provider.Markup.Should().NotContain("daily-heatmap-shell");
        provider.Markup.Should().NotContain("completed in this period");
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
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Morning Run · 1 completed"), TimeSpan.FromSeconds(5));

        // Assert
        provider.Markup.Should().Contain("2026");
        provider.Markup.Should().NotContain("2025");
    }
}
