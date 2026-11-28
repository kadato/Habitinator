#pragma warning disable MUD0012

using App.Shared.RCL.Components;
using App.Shared.RCL.Components.Dialogs;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

namespace App.Shared.RCL.Tests;

public sealed class HelpDialogTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();

    public HelpDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Renders_Shortcuts_And_Quick_Add_Syntax()
    {
        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        await provider.InvokeAsync(async () =>
            await dialogService.ShowAsync<HelpDialog>(string.Empty, DialogDefaults.Wide));
        await provider.WaitForStateAsync(
            () => provider.Markup.Contains("Keyboard shortcuts and quick add"),
            TimeSpan.FromSeconds(5));

        provider.Markup.Should().Contain("Navigation");
        provider.Markup.Should().Contain("Creating and editing");
        provider.Markup.Should().Contain("Board and timer");
        provider.Markup.Should().Contain("Open the command palette");
        provider.Markup.Should().Contain("New to-do");
        provider.Markup.Should().Contain("due:friday");
        provider.Markup.Should().Contain("!urgent / !p1 / !high");
        provider.Markup.Should().Contain("Shortcuts can be turned off in Settings.");
        provider.FindAll("kbd").Should().NotBeEmpty();
    }
}
