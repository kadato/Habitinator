using App.Shared.RCL.Components;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.CommandPalette;

using Bunit;

using FluentAssertions;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class CommandPaletteTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IBoardDataService _boardData = Substitute.For<IBoardDataService>();
    private readonly GlobalTimerService _timer = new(new SystemClock());
    private readonly IUndoService _undo = Substitute.For<IUndoService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IUserPreferencesService _preferences = Substitute.For<IUserPreferencesService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();
    private readonly CommandPaletteService _paletteService;

    public CommandPaletteTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();

        _preferences.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(UserPreferences.CreateDefault()));

        _ctx.Services.AddSingleton(_boardData);
        _ctx.Services.AddSingleton(_timer);
        _ctx.Services.AddSingleton(_undo);
        _ctx.Services.AddSingleton(_dialogs);
        _ctx.Services.AddSingleton(_preferences);
        _ctx.Services.AddSingleton(_notifier);
        _ctx.Services.AddScoped<CommandPaletteService>();
        _ctx.Services.AddScoped<ICommandPaletteService>(sp => sp.GetRequiredService<CommandPaletteService>());

        _paletteService = _ctx.Services.GetRequiredService<CommandPaletteService>();
    }

    public async ValueTask DisposeAsync()
    {
        _timer.Dispose();
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void PaletteService_Toggle_ChangesIsOpen()
    {
        _paletteService.IsOpen.Should().BeFalse();

        _paletteService.Open();
        _paletteService.IsOpen.Should().BeTrue();

        _paletteService.Close();
        _paletteService.IsOpen.Should().BeFalse();

        _paletteService.Toggle();
        _paletteService.IsOpen.Should().BeTrue();

        _paletteService.Toggle();
        _paletteService.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task GetRootCommandsAsync_Returns_CuratedCommands()
    {
        var rootCommands = await _paletteService.GetRootCommandsAsync();

        rootCommands.Should().NotBeEmpty();
        rootCommands.Should().Contain(c => c.Id == "create-todo" && c.Category == "Suggested");
        rootCommands.Should().Contain(c => c.Id == "create-habit" && c.Category == "Suggested");
        rootCommands.Should().Contain(c => c.Id == "create-daily" && c.Category == "Suggested");
        rootCommands.Should().Contain(c => c.Id == "nav-board" && c.Category == "Navigation");
        rootCommands.Should().Contain(c => c.Id == "nav-stats" && c.Category == "Navigation");
        rootCommands.Should().Contain(c => c.Id == "nav-settings" && c.Category == "Navigation");
        rootCommands.Should().Contain(c => c.Id == "theme-dark" && c.Category == "Appearance");
    }

    [Fact]
    public async Task SearchBoardItemsAsync_Returns_MatchingItems_With_SubActions()
    {
        var habitId = Guid.NewGuid();
        var habit = new BoardItem(habitId, "Morning Meditation", Counter: 5);
        var snapshot = new BoardSnapshot([habit], [], []);

        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        var results = await _paletteService.SearchBoardItemsAsync("meditation");

        results.Should().HaveCount(1);
        var item = results[0];
        item.Title.Should().Be("Morning Meditation");
        item.Category.Should().Be("Habits");
        item.ChildrenProvider.Should().NotBeNull();

        var subActions = await item.ChildrenProvider!();
        subActions.Should().Contain(a => a.Title.Contains("Increment"));
        subActions.Should().Contain(a => a.Title.Contains("Decrement"));
        subActions.Should().Contain(a => a.Title.Contains("Edit"));
        subActions.Should().Contain(a => a.Title.Contains("Archive"));
    }

    [Fact]
    public void CommandPalette_Renders_Only_When_Open()
    {
        var cut = _ctx.Render<CommandPalette>();
        cut.Markup.Should().BeEmpty();

        _paletteService.Open();
        cut.Render();

        cut.Find(".cmd-palette-dialog").Should().NotBeNull();
        cut.Find(".cmd-palette-input").Should().NotBeNull();

        _paletteService.Close();
        cut.Render();
        cut.Markup.Should().BeEmpty();
    }

    [Fact]
    public void CommandPalette_Escape_ClosesPalette_When_Query_Empty()
    {
        var cut = _ctx.Render<CommandPalette>();
        _paletteService.Open();
        cut.Render();

        var input = cut.Find(".cmd-palette-input");
        input.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        _paletteService.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void CommandPalette_InitialState_SelectsFirstItem()
    {
        var cut = _ctx.Render<CommandPalette>();
        _paletteService.Open();
        cut.Render();

        var selected = cut.Find(".cmd-palette-item--selected");
        selected.Should().NotBeNull();
        selected.GetAttribute("aria-selected").Should().Be("true");

        var items = cut.FindAll(".cmd-palette-item");
        items.Count.Should().BeGreaterThan(0);
        items[0].ClassList.Should().Contain("cmd-palette-item--selected");
    }

    [Fact]
    public void CommandPalette_ArrowDown_UpdatesSelectedItem()
    {
        var cut = _ctx.Render<CommandPalette>();
        _paletteService.Open();
        cut.Render();

        var input = cut.Find(".cmd-palette-input");
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        var items = cut.FindAll(".cmd-palette-item");
        items.Count.Should().BeGreaterThan(1);
        items[0].ClassList.Should().NotContain("cmd-palette-item--selected");
        items[1].ClassList.Should().Contain("cmd-palette-item--selected");
        items[1].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void CommandPalette_Renders_Distinct_Shortcut_Badges()
    {
        var cut = _ctx.Render<CommandPalette>();
        _paletteService.Open();
        cut.Render();

        var habitItem = cut.Find("#cmd-item-create-habit");
        var badges = habitItem.QuerySelectorAll(".cmd-palette-kbd");
        badges.Should().HaveCount(2);
        badges[0].TextContent.Trim().Should().Be("Ctrl");
        badges[1].TextContent.Trim().Should().Be("H");
    }
}
