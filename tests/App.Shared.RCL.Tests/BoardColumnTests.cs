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

public sealed class BoardColumnTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IBoardDataService _boardData = Substitute.For<IBoardDataService>();
    private readonly IDialogService _dialogService = Substitute.For<IDialogService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();
    private readonly IUserTimeZoneService _timeZone = Substitute.For<IUserTimeZoneService>();
    private readonly IUserDateFormatService _dateFormat = Substitute.For<IUserDateFormatService>();
    private readonly IUndoService _undoService = Substitute.For<IUndoService>();
    private readonly IBoardColumnStateStore _columnState = Substitute.For<IBoardColumnStateStore>();

    public BoardColumnTests()
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
    public void Updates_Visible_Items_When_Item_Title_Changes_Without_Count_Change()
    {
        var item = new BoardItem(Guid.NewGuid(), "Original Habit Title");
        var cut = _ctx.Render<BoardColumn>(parameters => parameters
            .Add(p => p.Section, BoardSection.Habit)
            .Add(p => p.Items, [item]));

        cut.Markup.Should().Contain("Original Habit Title");

        var updatedItem = item with { Title = "Updated Habit Title" };
        cut.Render(parameters => parameters
            .Add(p => p.Items, [updatedItem]));

        cut.Markup.Should().NotContain("Original Habit Title");
        cut.Markup.Should().Contain("Updated Habit Title");
    }

    [Fact]
    public void Updates_Visible_Items_When_Todo_Completed_State_Changes()
    {
        var todo = new BoardItem(Guid.NewGuid(), "My Todo", IsCompleted: false);
        var cut = _ctx.Render<BoardColumn>(parameters => parameters
            .Add(p => p.Section, BoardSection.Todo)
            .Add(p => p.Items, [todo]));

        cut.Markup.Should().Contain("My Todo");

        // When marked completed, it should be filtered out from the default "Active" filter
        var completedTodo = todo with { IsCompleted = true };
        cut.Render(parameters => parameters
            .Add(p => p.Items, [completedTodo]));

        cut.Markup.Should().NotContain("board-row--done");
        cut.Markup.Should().Contain("\"Active\" hides 1.");
    }
}
