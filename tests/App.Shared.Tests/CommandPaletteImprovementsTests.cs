using App.Shared.RCL.Services;
using App.Shared.RCL.Services.CommandPalette;

using MudBlazor;

using NSubstitute;

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

    [Fact]
    public async Task Recent_group_resolves_board_items_from_known_items()
    {
        using var timer = new GlobalTimerService(new SystemClock());
        var boardData = NSubstitute.Substitute.For<IBoardDataService>();
        var nav = NSubstitute.Substitute.For<Microsoft.AspNetCore.Components.NavigationManager>();
        var habitId = Guid.NewGuid();
        var habit = new App.Shared.RCL.Models.BoardItem(habitId, "Morning Run", Counter: 3);
        boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new App.Shared.RCL.Models.BoardSnapshot([habit], [], [])));

        var service = new CommandPaletteService(
            nav,
            boardData,
            timer,
            NSubstitute.Substitute.For<IUndoService>(),
            NSubstitute.Substitute.For<IDialogService>(),
            NSubstitute.Substitute.For<IUserPreferencesService>(),
            NSubstitute.Substitute.For<IUserNotifier>());

        // Search populates _knownItems cache
        var found = await service.SearchBoardItemsAsync("Morning");
        Assert.Single(found);

        service.RecordRecent(found[0].Id);

        // BuildRecentGroup without passing board items in root should still resolve from known cache
        var recent = service.BuildRecentGroup([]);
        Assert.Single(recent);
        Assert.Equal("Recent", recent[0].Category);
        Assert.Equal(found[0].Id, recent[0].Id);
        Assert.Equal("Morning Run", recent[0].Title);
    }

    [Fact]
    public void ParseInlineTitle_extracts_due_dates_and_tags()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tomorrow = today.AddDays(1);

        var (t1, tags1, due1) = BoardPaletteCommands.ParseInlineTitle("Buy groceries #groceries due:tomorrow");
        Assert.Equal("Buy groceries", t1);
        Assert.Equal("groceries", tags1);
        Assert.Equal(tomorrow, due1);

        var (t2, tags2, due2) = BoardPaletteCommands.ParseInlineTitle("Submit report @today !urgent");
        Assert.Equal("Submit report", t2);
        Assert.Equal("urgent", tags2);
        Assert.Equal(today, due2);

        var (t3, tags3, due3) = BoardPaletteCommands.ParseInlineTitle("Project deadline due:2026-12-25 #work");
        Assert.Equal("Project deadline", t3);
        Assert.Equal("work", tags3);
        Assert.Equal(new DateOnly(2026, 12, 25), due3);
    }

    [Fact]
    public void BuildQuickAddCommands_enriches_subtitle_with_tags_and_due_date()
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

        var results = service.BuildQuickAddCommands("add todo Pay bills #finance due:tomorrow");
        Assert.Single(results);
        Assert.Contains("Pay bills", results[0].Title, StringComparison.Ordinal);
        Assert.NotNull(results[0].Subtitle);
        Assert.Contains("#finance", results[0].Subtitle, StringComparison.Ordinal);
        Assert.Contains("Due:", results[0].Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandItem_PrimaryAction_executes_separately()
    {
        var executedPrimary = false;
        var executedStandard = false;

        var item = new CommandItem(
            Id: "test-item",
            Title: "Test item",
            Subtitle: null,
            Category: "Test",
            Icon: Icons.Material.Filled.Check,
            Action: () =>
            {
                executedStandard = true;
                return Task.CompletedTask;
            },
            PrimaryAction: () =>
            {
                executedPrimary = true;
                return Task.CompletedTask;
            });

        Assert.NotNull(item.PrimaryAction);
        await item.PrimaryAction();
        Assert.True(executedPrimary);
        Assert.False(executedStandard);

        Assert.NotNull(item.Action);
        await item.Action();
        Assert.True(executedStandard);
    }
}
