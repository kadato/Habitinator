#pragma warning disable MUD0012

using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class SyncConflictsDialogTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IUserDateFormatService _dateFormat = Substitute.For<IUserDateFormatService>();

    public SyncConflictsDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_dateFormat);
        _dateFormat.Format(Arg.Any<DateTime>()).Returns("2026-10-03");
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Renders_Each_Conflict_With_Outcome_And_Fields()
    {
        var conflicts = new List<BoardSyncConflict>
        {
            new(new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero), Guid.NewGuid(), "Run", BoardSyncConflictOutcome.KeptDevice, "title, counter"),
            new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero), Guid.NewGuid(), "Read", BoardSyncConflictOutcome.Identical, string.Empty)
        };
        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<SyncConflictsDialog>
        {
            { x => x.Conflicts, conflicts }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<SyncConflictsDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Run"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().Contain("Sync conflicts");
        provider.Markup.Should().Contain("Run");
        provider.Markup.Should().Contain("Kept this device");
        provider.Markup.Should().Contain("Changed: title, counter");
        provider.Markup.Should().Contain("Same on both sides");
        provider.Markup.Should().Contain("No field differences");
        provider.Markup.Should().Contain("2026-10-03");
    }
}
