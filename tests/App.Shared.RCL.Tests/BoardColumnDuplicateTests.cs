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

public sealed class BoardColumnDuplicateTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IBoardDataService _boardData = Substitute.For<IBoardDataService>();
    private readonly IDialogService _dialogService = Substitute.For<IDialogService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();
    private readonly IUserTimeZoneService _timeZone = Substitute.For<IUserTimeZoneService>();
    private readonly IUserDateFormatService _dateFormat = Substitute.For<IUserDateFormatService>();
    private readonly IUndoService _undoService = Substitute.For<IUndoService>();
    private readonly IBoardColumnStateStore _columnState = Substitute.For<IBoardColumnStateStore>();

    public BoardColumnDuplicateTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_boardData);
        _ctx.Services.AddSingleton(_dialogService);
        _ctx.Services.AddSingleton(_notifier);
        _ctx.Services.AddSingleton(_timeZone);
        _ctx.Services.AddSingleton(_dateFormat);
        _ctx.Services.AddSingleton(_undoService);
        _ctx.Services.AddSingleton(_columnState);

        _columnState.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardColumnFilterState?>(null));

        _dateFormat.Format(Arg.Any<DateOnly>())
            .Returns(x => ((DateOnly)x[0]!).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        _ = _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void Duplicate_Title_Shows_Warning_Before_Creating()
    {
        var existing = new BoardItem(Guid.NewGuid(), "Resume and portfolio polish");
        var cut = _ctx.Render<BoardColumn>(parameters => parameters
            .Add(p => p.Section, BoardSection.Todo)
            .Add(p => p.Items, [existing]));

        cut.Find("input").Input("Resume and portfolio polish");
        cut.Find("button[aria-label='Add todo']").Click();

        cut.Markup.Should().Contain("Already exists");
        cut.Markup.Should().Contain("Resume and portfolio polish");
        _boardData.DidNotReceiveWithAnyArgs().CreateItemAsync(
            Arg.Any<BoardSection>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Create_Anyway_Creates_Duplicate_Title()
    {
        var created = new BoardItem(Guid.NewGuid(), "Resume and portfolio polish");
        _boardData.CreateItemAsync(
                Arg.Any<BoardSection>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(created);

        var existing = new BoardItem(Guid.NewGuid(), "Resume and portfolio polish");
        var cut = _ctx.Render<BoardColumn>(parameters => parameters
            .Add(p => p.Section, BoardSection.Todo)
            .Add(p => p.Items, [existing]));

        cut.Find("input").Input("Resume and portfolio polish");
        cut.Find("button[aria-label='Add todo']").Click();
        cut.Markup.Should().Contain("Already exists");

        cut.FindAll("button").First(b => b.TextContent.Contains("Add anyway", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => _boardData.ReceivedWithAnyArgs(1).CreateItemAsync(
            Arg.Any<BoardSection>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Distinct_Title_Creates_Without_Warning()
    {
        var existing = new BoardItem(Guid.NewGuid(), "Batch 1 abc12345");
        _boardData.CreateItemAsync(
                Arg.Any<BoardSection>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new BoardItem(Guid.NewGuid(), "Batch 2 abc12345"));

        var cut = _ctx.Render<BoardColumn>(parameters => parameters
            .Add(p => p.Section, BoardSection.Todo)
            .Add(p => p.Items, [existing]));

        cut.Find("input").Input("Batch 2 abc12345");
        cut.Find("button[aria-label='Add todo']").Click();

        cut.WaitForAssertion(() => _boardData.ReceivedWithAnyArgs(1).CreateItemAsync(
            Arg.Any<BoardSection>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()));
        cut.Markup.Should().NotContain("Already exists");
    }
}
