#pragma warning disable MUD0012

using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Board.Local;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class PendingOutboxDialogTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IBoardSyncRequestor _syncRequestor = Substitute.For<IBoardSyncRequestor>();

    public PendingOutboxDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_syncRequestor);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Renders_HumanReadable_Descriptions_For_Pending_Operations()
    {
        var pending = new List<BoardOutboxEntry>
        {
            new()
            {
                OperationId = Guid.NewGuid(),
                Kind = BoardOutboxOperationKind.Create,
                PayloadJson = "{\"Section\":0,\"Title\":\"Drink Water\",\"ClientItemId\":\"00000000-0000-0000-0000-000000000001\"}",
                AttemptCount = 0
            },
            new()
            {
                OperationId = Guid.NewGuid(),
                Kind = BoardOutboxOperationKind.UpdateDaily,
                PayloadJson = "{\"ItemId\":\"00000000-0000-0000-0000-000000000002\",\"Title\":\"Morning Jog\"}",
                AttemptCount = 8
            }
        };

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<PendingOutboxDialog>
        {
            { x => x.Pending, pending }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<PendingOutboxDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Drink Water"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().Contain("Created \"Drink Water\"");
        provider.Markup.Should().Contain("Waiting");
        provider.Markup.Should().Contain("Updated daily \"Morning Jog\"");
        provider.Markup.Should().Contain("Paused");
        provider.Markup.Should().Contain("Sync all now");
    }
}
