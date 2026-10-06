using App.Shared.RCL.Components;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class BoardItemCardOverdueTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IUserTimeZoneService _timeZoneService = Substitute.For<IUserTimeZoneService>();
    private readonly IUserDateFormatService _dateFormatService = Substitute.For<IUserDateFormatService>();

    public BoardItemCardOverdueTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_timeZoneService);
        _ctx.Services.AddSingleton(_dateFormatService);
        _dateFormatService.Format(Arg.Any<DateOnly>()).Returns("2026-08-11");
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void Renders_Overdue_Pill_With_Since_Date_For_Daily()
    {
        var item = new BoardItem(Guid.NewGuid(), "Gym", DailyStartDate: new DateOnly(2026, 8, 3));

        var cut = _ctx.Render<BoardItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.Section, BoardSection.Daily)
            .Add(p => p.OverdueSince, new DateOnly(2026, 8, 11)));

        cut.Markup.Should().Contain("board-daily-overdue");
        cut.Markup.Should().Contain("Overdue since 2026-08-11");
    }

    [Fact]
    public void Omits_Overdue_Pill_When_Not_Overdue()
    {
        var item = new BoardItem(Guid.NewGuid(), "Gym", DailyStartDate: new DateOnly(2026, 8, 3));

        var cut = _ctx.Render<BoardItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.Section, BoardSection.Daily));

        cut.Markup.Should().NotContain("board-daily-overdue");
    }
}
