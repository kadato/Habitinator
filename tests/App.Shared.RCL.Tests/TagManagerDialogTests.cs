#pragma warning disable MUD0012

using App.Shared.RCL.Components;
using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class TagManagerDialogTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IBoardDataService _board = Substitute.For<IBoardDataService>();
    private readonly IUndoService _undo = Substitute.For<IUndoService>();
    private readonly List<(string Description, IDisposable Batch)> _batches = [];

    private static readonly Guid HabitId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DailyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TodoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ArchivedId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public TagManagerDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_board);
        _ctx.Services.AddSingleton(_undo);
        _ctx.Services.AddSingleton<ILogger<TagManagerDialog>>(NullLogger<TagManagerDialog>.Instance);
        _ctx.Render<MudPopoverProvider>();

        _board.GetSnapshotAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(BuildActiveSnapshot()));
        _board.GetArchivedSnapshotAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(BuildArchivedSnapshot()));
        _undo.BeginBatch(Arg.Any<string>()).Returns(call =>
        {
            var batch = Substitute.For<IDisposable>();
            _batches.Add((call.ArgAt<string>(0), batch));
            return batch;
        });

        _board.UpdateHabitAsync(Arg.Any<Guid>(), Arg.Any<UpdateHabitArgs>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(Item(HabitId, "Read", "tag")));
        _board.UpdateDailyAsync(Arg.Any<Guid>(), Arg.Any<UpdateDailyArgs>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(Item(DailyId, "Run", "tag")));
        _board.UpdateTodoAsync(Arg.Any<Guid>(), Arg.Any<UpdateTodoArgs>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(Item(TodoId, "Report", "tag")));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Lists_every_tag_once_with_its_item_count()
    {
        var provider = await OpenDialogAsync();

        await provider.WaitForAssertionAsync(() =>
        {
            provider.Markup.Should().Contain("health");
            provider.Markup.Should().Contain("work");
            provider.Markup.Should().Contain("3 items");
        });
    }

    [Fact]
    public async Task Renames_a_tag_on_live_and_archived_items_in_one_undo_batch()
    {
        var provider = await OpenDialogAsync();

        await BeginRenameAsync(provider, "health");
        await SetRenameValueAsync(provider, "wellness");
        await ClickButtonAsync(provider, "Save");

        var batch = ReceivedBatch("Rename tag \"health\"");
        await _board.Received(1).UpdateHabitAsync(
            HabitId,
            Arg.Is<UpdateHabitArgs>(a => a.Tags == "wellness,work"),
            Arg.Any<CancellationToken>());
        await _board.Received(1).UpdateDailyAsync(
            DailyId,
            Arg.Is<UpdateDailyArgs>(a => a.Tags == "wellness"),
            Arg.Any<CancellationToken>());
        await _board.Received(1).UpdateHabitAsync(
            ArchivedId,
            Arg.Is<UpdateHabitArgs>(a => a.Tags == "wellness"),
            Arg.Any<CancellationToken>());
        await _board.DidNotReceive().UpdateTodoAsync(
            Arg.Any<Guid>(),
            Arg.Any<UpdateTodoArgs>(),
            Arg.Any<CancellationToken>());

        batch.Received(1).Dispose();
        await provider.WaitForAssertionAsync(() => provider.Markup.Should().Contain("Renamed \"health\" to \"wellness\" on 3 items."));
    }

    [Fact]
    public async Task Renaming_into_an_existing_tag_merges_without_duplicates()
    {
        var provider = await OpenDialogAsync();

        await BeginRenameAsync(provider, "health");
        await SetRenameValueAsync(provider, "work");
        await ClickButtonAsync(provider, "Save");

        await _board.Received(1).UpdateHabitAsync(
            HabitId,
            Arg.Is<UpdateHabitArgs>(a => a.Tags == "work"),
            Arg.Any<CancellationToken>());
        await _board.Received(1).UpdateDailyAsync(
            DailyId,
            Arg.Is<UpdateDailyArgs>(a => a.Tags == "work"),
            Arg.Any<CancellationToken>());
        await provider.WaitForAssertionAsync(() => provider.Markup.Should().Contain("Merged \"health\" into \"work\""));
    }

    [Fact]
    public async Task Deleting_a_tag_asks_first_and_clears_it_everywhere()
    {
        var provider = await OpenDialogAsync();

        await ClickButtonAsync(provider, "Delete tag health");

        await provider.WaitForAssertionAsync(() => provider.Markup.Should().Contain("Remove \"health\" from 3 items?"));
        await ClickButtonAsync(provider, "Delete tag");

        var batch = ReceivedBatch("Delete tag \"health\"");
        await _board.Received(1).UpdateHabitAsync(
            HabitId,
            Arg.Is<UpdateHabitArgs>(a => a.Tags == "work"),
            Arg.Any<CancellationToken>());
        await _board.Received(1).UpdateDailyAsync(
            DailyId,
            Arg.Is<UpdateDailyArgs>(a => a.Tags == null),
            Arg.Any<CancellationToken>());
        batch.Received(1).Dispose();
        await provider.WaitForAssertionAsync(() => provider.Markup.Should().Contain("Removed \"health\" from 3 items."));
    }

    [Fact]
    public async Task Rejects_an_empty_name_and_a_name_with_a_comma()
    {
        var provider = await OpenDialogAsync();

        await BeginRenameAsync(provider, "health");
        await SetRenameValueAsync(provider, "   ");
        await ClickButtonAsync(provider, "Save");

        await provider.WaitForAssertionAsync(() => provider.Markup.Should().Contain("Enter a tag name."));

        await SetRenameValueAsync(provider, "health,med");
        await ClickButtonAsync(provider, "Save");

        await provider.WaitForAssertionAsync(() => provider.Markup.Should().Contain("Tags cannot contain commas."));
        await _board.DidNotReceive().UpdateHabitAsync(
            Arg.Any<Guid>(),
            Arg.Any<UpdateHabitArgs>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_item_update_reports_a_partial_result()
    {
        _board.UpdateDailyAsync(Arg.Any<Guid>(), Arg.Any<UpdateDailyArgs>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(null));
        var provider = await OpenDialogAsync();

        await BeginRenameAsync(provider, "health");
        await SetRenameValueAsync(provider, "wellness");
        await ClickButtonAsync(provider, "Save");

        await provider.WaitForAssertionAsync(() =>
            provider.Markup.Should().Contain("Renamed \"health\" on 2 of 3 items. Retry the rest."));
    }

    [Fact]
    public async Task A_truncated_snapshot_is_not_editable()
    {
        _board.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BuildActiveSnapshot() with { Truncated = true }));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogService.ShowAsync<TagManagerDialog>(string.Empty, DialogDefaults.Wide));
        await provider.WaitForElementAsync("[data-dialog-label='Manage tags']");

        await provider.WaitForAssertionAsync(() =>
            provider.Markup.Should().Contain("The board is still loading. Try again in a moment."));
        provider.FindAll("button[aria-label^='Rename tag']").Should().BeEmpty();
    }

    private async Task<IRenderedComponent<MudDialogProvider>> OpenDialogAsync()
    {
        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogService.ShowAsync<TagManagerDialog>(string.Empty, DialogDefaults.Wide));
        await provider.WaitForElementAsync("[data-dialog-label='Manage tags']");
        await provider.WaitForAssertionAsync(() => provider.Markup.Should().Contain("health"));
        return provider;
    }

    private static async Task BeginRenameAsync(IRenderedComponent<MudDialogProvider> provider, string tag)
    {
        var row = provider.FindAll(".tag-manager__item")
            .Single(e => e.TextContent.Contains(tag, StringComparison.Ordinal));
        var button = row.QuerySelector($"button[aria-label='Rename tag {tag}']")
            ?? throw new InvalidOperationException($"Rename button for {tag} was not found.");
        await provider.InvokeAsync(() => button.Click());
        await provider.WaitForAssertionAsync(() =>
            _ = provider.Find($"input[aria-label='New name for tag {tag}']"));
    }

    private static async Task SetRenameValueAsync(IRenderedComponent<MudDialogProvider> provider, string value)
    {
        var input = provider.Find(".tag-manager__input");
        await provider.InvokeAsync(() => input.Input(value));
    }

    private static async Task ClickButtonAsync(IRenderedComponent<MudDialogProvider> provider, string text)
    {
        var button = provider.FindAll("button")
            .First(b => string.Equals(b.TextContent.Trim(), text, StringComparison.Ordinal)
                        || string.Equals(b.GetAttribute("aria-label"), text, StringComparison.Ordinal));
        await provider.InvokeAsync(() => button.Click());
    }

    private IDisposable ReceivedBatch(string description)
    {
        var matches = _batches.Where(b => b.Description == description).ToList();
        matches.Should().ContainSingle($"one batch named {description} must be opened");
        return matches[0].Batch;
    }

    private static BoardSnapshot BuildActiveSnapshot() =>
        new(
            [Item(HabitId, "Read", "health,work")],
            [Item(DailyId, "Run", "health")],
            [Item(TodoId, "Report", "work")]);

    private static BoardSnapshot BuildArchivedSnapshot() =>
        new([Item(ArchivedId, "Old habit", "health", isArchived: true)], [], []);

    private static BoardItem Item(Guid id, string title, string? tags, bool isArchived = false) =>
        new(id, title, Tags: tags, IsArchived: isArchived);
}
