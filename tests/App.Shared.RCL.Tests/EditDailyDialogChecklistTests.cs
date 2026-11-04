#pragma warning disable MUD0012

using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class EditDailyDialogChecklistTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IActivityStatisticsReader _stats = Substitute.For<IActivityStatisticsReader>();
    private readonly IUserDateFormatService _dateFormat = Substitute.For<IUserDateFormatService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();
    private readonly IBoardDataService _boardData = Substitute.For<IBoardDataService>();
    private readonly IUndoService _undo = Substitute.For<IUndoService>();

    public EditDailyDialogChecklistTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_stats);
        _ctx.Services.AddSingleton(_dateFormat);
        _ctx.Services.AddSingleton<IUserNotifier>(_notifier);
        _ctx.Services.AddSingleton(_boardData);
        _ctx.Services.AddSingleton(_undo);

        _dateFormat.DateFormat.Returns("yyyy-MM-dd");
        _undo.BeginBatch(Arg.Any<string>()).Returns(Substitute.For<IDisposable>());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _stats.GetDailyContributionsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DailyContributionsViewDto("r370", [], [], today.AddDays(-30), today)));

        _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Shows_existing_subtasks_without_expanding()
    {
        var checklist = DailyChecklistJson.Serialize(
        [
            new DailyChecklistItem(Guid.NewGuid(), "Subtask one"),
            new DailyChecklistItem(Guid.NewGuid(), "Subtask two", true),
        ])!;
        var item = new BoardItem(Guid.NewGuid(), "Morning routine", ChecklistJson: checklist);

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogs = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<EditDailyDialog> { { x => x.Item, item } };

        await provider.InvokeAsync(async () => await dialogs.ShowAsync<EditDailyDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Subtask one"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().Contain("Subtask one");
        provider.Markup.Should().Contain("Subtask two");
        provider.Markup.Should().Contain("New checklist item");
    }

    [Fact]
    public async Task Shows_checklist_editor_when_empty()
    {
        var item = new BoardItem(Guid.NewGuid(), "Empty daily");

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogs = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<EditDailyDialog> { { x => x.Item, item } };

        await provider.InvokeAsync(async () => await dialogs.ShowAsync<EditDailyDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("New checklist item"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().Contain("New checklist item");
    }
}
