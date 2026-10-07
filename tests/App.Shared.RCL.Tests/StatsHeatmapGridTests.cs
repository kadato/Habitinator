#pragma warning disable MUD0012

using App.Shared.RCL.Components;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.AspNetCore.Components.Web;

using MudBlazor.Services;

namespace App.Shared.RCL.Tests;

public sealed class StatsHeatmapGridTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();

    public StatsHeatmapGridTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void Renders_One_Tab_Stop_On_Today()
    {
        var today = new DateOnly(2026, 10, 7);
        var cut = _ctx.Render<StatsHeatmapGrid>(p => p
            .Add(x => x.Columns, 2)
            .Add(x => x.Cells, CellsFor(today))
            .Add(x => x.IsTodayFunc, d => d == today)
            .Add(x => x.OnCellClick, (r, c) => Task.CompletedTask));

        var buttons = cut.FindAll(".stats-heatmap-day-btn");
        buttons.Should().HaveCount(14);
        buttons.Count(b => b.GetAttribute("tabindex") == "0").Should().Be(1);
        var focused = buttons.Single(b => b.GetAttribute("tabindex") == "0");
        focused.GetAttribute("data-row").Should().Be("3");
        focused.GetAttribute("data-col").Should().Be("0");
    }

    [Fact]
    public void ArrowRight_Moves_Tab_Stop_To_Next_Day()
    {
        var today = new DateOnly(2026, 10, 7);
        var cut = _ctx.Render<StatsHeatmapGrid>(p => p
            .Add(x => x.Columns, 2)
            .Add(x => x.Cells, CellsFor(today))
            .Add(x => x.IsTodayFunc, d => d == today)
            .Add(x => x.OnCellClick, (r, c) => Task.CompletedTask));

        cut.Find(".stats-heatmap-grid").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        var buttons = cut.FindAll(".stats-heatmap-day-btn");
        buttons.Count(b => b.GetAttribute("tabindex") == "0").Should().Be(1);
        var focused = buttons.Single(b => b.GetAttribute("tabindex") == "0");
        focused.GetAttribute("data-row").Should().Be("3");
        focused.GetAttribute("data-col").Should().Be("1");
    }

    private static Dictionary<(int R, int C), ActivityHeatmapCellDto> CellsFor(DateOnly today)
    {
        var cells = new Dictionary<(int R, int C), ActivityHeatmapCellDto>();
        for (var r = 0; r < 7; r++)
        {
            for (var c = 0; c < 2; c++)
            {
                var date = today.AddDays(c * 7 + r - 3);
                cells[(r, c)] = new ActivityHeatmapCellDto(r, c, date, 1, 2, true);
            }
        }

        return cells;
    }
}
