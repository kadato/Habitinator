#pragma warning disable MUD0012

using App.Shared.RCL.Components;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
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
    private readonly IUpcomingViewStateStore _viewState = Substitute.For<IUpcomingViewStateStore>();
    private readonly IUserPreferencesService _prefs = Substitute.For<IUserPreferencesService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IUndoService _undo = Substitute.For<IUndoService>();

    public UpcomingPanelTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_boardData);
        _ctx.Services.AddSingleton(_timeZone);
        _ctx.Services.AddSingleton(_dateFormat);
        _ctx.Services.AddSingleton(_notifier);
        _ctx.Services.AddSingleton(_viewState);
        _ctx.Services.AddSingleton(_prefs);
        _ctx.Services.AddSingleton(_dialogs);
        _ctx.Services.AddSingleton(_undo);
        _ctx.Render<MudPopoverProvider>();
        _viewState.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<UpcomingViewState?>(null));
        _prefs.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(UserPreferences.CreateDefault()));
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
        // Month grid shows neighbouring days (Tomorrow) and chip hints (Mon, Wed).
        _viewState.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UpcomingViewState?>(new UpcomingViewState("Month", "All")));

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

    [Fact]
    public void Restores_Persisted_View_And_Filter()
    {
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [], [])));
        _viewState.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UpcomingViewState?>(new UpcomingViewState("Week", "Dailies")));

        var cut = _ctx.Render<UpcomingPanel>();
        cut.WaitForState(() => cut.Markup.Contains("Nothing scheduled"), TimeSpan.FromSeconds(5));

        var weekBtn = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Week");
        weekBtn.ClassList.Should().Contain("cal-seg__btn--active");
        var dailiesBtn = cut.FindAll("button").Single(b => b.TextContent.Contains("Dailies"));
        dailiesBtn.ClassList.Should().Contain("cal-seg__btn--active");
    }

    [Fact]
    public void Persists_View_And_Filter_On_Tab_Click()
    {
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [], [])));

        var cut = _ctx.Render<UpcomingPanel>();
        cut.WaitForState(() => cut.Markup.Contains("Nothing scheduled"), TimeSpan.FromSeconds(5));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Week").Click();
        cut.WaitForAssertion(() =>
            _viewState.Received().SetAsync(
                Arg.Is<UpcomingViewState>(s => s.View == "Week"),
                Arg.Any<CancellationToken>()),
            TimeSpan.FromSeconds(5));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Dailies")).Click();
        cut.WaitForAssertion(() =>
            _viewState.Received().SetAsync(
                Arg.Is<UpcomingViewState>(s => s.Filter == "Dailies"),
                Arg.Any<CancellationToken>()),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Week_Selecting_Day_Keeps_Window_And_Moves_Agenda()
    {
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [], [])));
        _viewState.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UpcomingViewState?>(new UpcomingViewState("Week", "All")));

        var cut = _ctx.Render<UpcomingPanel>();
        cut.WaitForState(() => cut.FindAll(".cal-week-grid button").Count == 7, TimeSpan.FromSeconds(5));
        var before = cut.FindAll(".cal-week-grid button")
            .Select(b => b.GetAttribute("aria-label")).ToList();

        // Tap the third visible day. The grid window must not move, only the agenda.
        cut.FindAll(".cal-week-grid button")[2].Click();

        var afterCells = cut.FindAll(".cal-week-grid button");
        afterCells.Select(b => b.GetAttribute("aria-label")).Should().BeEquivalentTo(before);
        afterCells[2].GetAttribute("aria-current").Should().Be("date");
    }

    [Fact]
    public void Week_Top_Nav_Moves_Window()
    {
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [], [])));
        _viewState.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UpcomingViewState?>(new UpcomingViewState("Week", "All")));

        var cut = _ctx.Render<UpcomingPanel>();
        cut.WaitForState(() => cut.FindAll(".cal-week-grid button").Count == 7, TimeSpan.FromSeconds(5));
        var firstBefore = ExtractDate(cut.FindAll(".cal-week-grid button")[0].GetAttribute("aria-label"));

        cut.Find("button[aria-label='Next week']").Click();

        cut.WaitForAssertion(() =>
        {
            var firstAfter = ExtractDate(cut.FindAll(".cal-week-grid button")[0].GetAttribute("aria-label"));
            firstAfter.Should().Be(firstBefore.AddDays(7));
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Retired_Next7_Maps_To_Week()
    {
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [], [])));
        _viewState.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UpcomingViewState?>(new UpcomingViewState("Next7", "All")));

        var cut = _ctx.Render<UpcomingPanel>();
        cut.WaitForState(() => cut.Markup.Contains("Nothing scheduled"), TimeSpan.FromSeconds(5));

        var weekBtn = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Week");
        weekBtn.ClassList.Should().Contain("cal-seg__btn--active");
    }

    private static DateOnly ExtractDate(string? ariaLabel)
    {
        var match = System.Text.RegularExpressions.Regex.Match(ariaLabel ?? string.Empty, @"\d{4}-\d{2}-\d{2}");
        match.Success.Should().BeTrue("cell aria-label should contain the formatted date");
        return DateOnly.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Ignores_Invalid_Persisted_Values()
    {
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [], [])));
        _viewState.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UpcomingViewState?>(new UpcomingViewState("Nope", "Bogus")));

        var cut = _ctx.Render<UpcomingPanel>();
        cut.WaitForState(() => cut.Markup.Contains("Nothing scheduled"), TimeSpan.FromSeconds(5));

        // Defaults are Day and All.
        var dayBtn = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Day");
        dayBtn.ClassList.Should().Contain("cal-seg__btn--active");
        var allBtn = cut.FindAll("button").Single(b => b.TextContent.Contains("All ("));
        allBtn.ClassList.Should().Contain("cal-seg__btn--active");
    }
}
