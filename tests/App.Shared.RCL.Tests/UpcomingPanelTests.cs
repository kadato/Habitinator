#pragma warning disable MUD0012

using App.Shared.RCL.Components;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class UpcomingPanelTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IBoardDataService _boardData = Substitute.For<IBoardDataService>();
    private readonly IUserTimeZoneService _timeZone = Substitute.For<IUserTimeZoneService>();
    private readonly IUserDateFormatService _dateFormat = Substitute.For<IUserDateFormatService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();

    public UpcomingPanelTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_boardData);
        _ctx.Services.AddSingleton(_timeZone);
        _ctx.Services.AddSingleton(_dateFormat);
        _ctx.Services.AddSingleton(_notifier);
        _dateFormat.Format(Arg.Any<DateOnly>()).Returns(x => ((DateOnly)x[0]!).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void Renders_Overdue_Today_And_Future_With_Schedule_Reasons()
    {
        var today = new DateOnly(2024, 1, 8); // Monday
        var overdue = new BoardItem(Guid.NewGuid(), "Late todo", TodoDueDate: today.AddDays(-2));
        var daily = new BoardItem(
            Guid.NewGuid(), "Gym",
            DailyStartDate: new DateOnly(2024, 1, 1),
            DailyRepeat: DailyRepeatType.Weekly,
            DailyRepeatInterval: 1,
            DailyWeekdays: DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Wednesday));
        var snapshot = new BoardSnapshot([], [daily], [overdue]);
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(snapshot));

        var cut = _ctx.Render<UpcomingPanel>();
        cut.WaitForState(() => cut.Markup.Contains("Gym"), TimeSpan.FromSeconds(5));

        cut.Markup.Should().Contain("Overdue");
        cut.Markup.Should().Contain("Late todo");
        cut.Markup.Should().Contain("Today");
        cut.Markup.Should().Contain("Tomorrow");
        cut.Markup.Should().Contain("Mon, Wed");
    }

    [Fact]
    public void Renders_Empty_State_When_Nothing_Scheduled()
    {
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [], [])));

        var cut = _ctx.Render<UpcomingPanel>();
        cut.WaitForState(() => cut.Markup.Contains("Nothing scheduled"), TimeSpan.FromSeconds(5));

        cut.Markup.Should().Contain("Today");
        cut.Markup.Should().NotContain("Overdue");
    }

    [Fact]
    public void Today_Toggle_Calls_BoardData()
    {
        var daily = new BoardItem(
            Guid.NewGuid(), "Read",
            DailyStartDate: new DateOnly(2024, 1, 1),
            DailyRepeat: DailyRepeatType.Daily,
            DailyRepeatInterval: 1);
        var snapshot = new BoardSnapshot([], [daily], []);
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(snapshot));
        _boardData.ToggleItemAsync(BoardSection.Daily, daily.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(daily));

        var cut = _ctx.Render<UpcomingPanel>();
        cut.WaitForState(() => cut.Markup.Contains("Read"), TimeSpan.FromSeconds(5));

        var checkbox = cut.Find(".upcoming-check input");
        checkbox.Change(true);

        _boardData.Received(1).ToggleItemAsync(BoardSection.Daily, daily.Id, Arg.Any<CancellationToken>());
    }
}
