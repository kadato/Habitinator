using App.Shared.RCL.Services;
using App.Shared.RCL.Services.CommandPalette;

using MudBlazor;

namespace App.Shared.Tests;

public sealed class CommandPaletteScoringTests
{
    [Fact]
    public void Prefix_match_scores_highest()
    {
        var prefix = CommandPaletteScoring.Score("Morning Meditation", "Morning");
        var substring = CommandPaletteScoring.Score("Evening Morning Walk", "Morning");
        var fuzzy = CommandPaletteScoring.Score("Meditation", "mtn");

        Assert.True(prefix > substring);
        Assert.True(substring > 0);
        Assert.True(fuzzy > 0);
        Assert.Equal(0, CommandPaletteScoring.Score("Evening Walk", "xyz"));
    }

    [Fact]
    public void Item_scoring_prefers_title_over_keywords()
    {
        var item = new CommandItem("id", "Morning Run", "Subtitle", "Habits", Icons.Material.Filled.Repeat, Keywords: ["running"]);
        var titleScore = CommandPaletteScoring.ScoreItem(item, "Morning");
        var keywordScore = CommandPaletteScoring.ScoreItem(item, "running");

        Assert.True(titleScore > keywordScore);
    }
}

public sealed class CommandPaletteQuickAddTests
{
    [Fact]
    public void BuildQuickAddCommands_parses_section_and_title()
    {
        using var timer = new GlobalTimerService(new SystemClock());
        var boardData = NSubstitute.Substitute.For<IBoardDataService>();
        var nav = NSubstitute.Substitute.For<Microsoft.AspNetCore.Components.NavigationManager>();
        var service = new CommandPaletteService(
            nav,
            boardData,
            timer,
            NSubstitute.Substitute.For<IUndoService>(),
            NSubstitute.Substitute.For<IDialogService>(),
            NSubstitute.Substitute.For<IUserPreferencesService>(),
            NSubstitute.Substitute.For<IUserNotifier>());

        var results = service.BuildQuickAddCommands("add todo Buy milk #home");

        Assert.Single(results);
        Assert.Contains("Buy milk", results[0].Title, StringComparison.Ordinal);
        Assert.Equal("Quick add", results[0].Category);
    }

    [Fact]
    public void BuildQuickAddCommands_returns_empty_without_prefix()
    {
        using var timer = new GlobalTimerService(new SystemClock());
        var boardData = NSubstitute.Substitute.For<IBoardDataService>();
        var nav = NSubstitute.Substitute.For<Microsoft.AspNetCore.Components.NavigationManager>();
        var service = new CommandPaletteService(
            nav,
            boardData,
            timer,
            NSubstitute.Substitute.For<IUndoService>(),
            NSubstitute.Substitute.For<IDialogService>(),
            NSubstitute.Substitute.For<IUserPreferencesService>(),
            NSubstitute.Substitute.For<IUserNotifier>());

        Assert.Empty(service.BuildQuickAddCommands("Buy milk"));
        Assert.Empty(service.BuildQuickAddCommands("add "));
    }

    [Fact]
    public void Recent_group_maps_ids_to_recent_category()
    {
        using var timer = new GlobalTimerService(new SystemClock());
        var boardData = NSubstitute.Substitute.For<IBoardDataService>();
        var nav = NSubstitute.Substitute.For<Microsoft.AspNetCore.Components.NavigationManager>();
        var service = new CommandPaletteService(
            nav,
            boardData,
            timer,
            NSubstitute.Substitute.For<IUndoService>(),
            NSubstitute.Substitute.For<IDialogService>(),
            NSubstitute.Substitute.For<IUserPreferencesService>(),
            NSubstitute.Substitute.For<IUserNotifier>());

        service.RecordRecent("create-todo");
        service.RecordRecent("create-todo");
        Assert.Single(service.RecentIds);

        var root = new List<CommandItem>
        {
            new("create-todo", "New to-do", null, "Suggested", Icons.Material.Filled.CheckBoxOutlineBlank),
            new("nav-board", "Board", null, "Navigation", Icons.Material.Filled.SpaceDashboard)
        };
        var recent = service.BuildRecentGroup(root);

        Assert.Single(recent);
        Assert.Equal("Recent", recent[0].Category);
        Assert.Equal("create-todo", recent[0].Id);
    }
}
