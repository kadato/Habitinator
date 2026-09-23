#pragma warning disable MUD0012

using App.Shared.RCL.Components.Dialogs;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

namespace App.Shared.RCL.Tests;

public sealed class SetFocusDialogsTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();

    public SetFocusDialogsTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task SetFocusDurationDialog_Renders_With_Autofocus_And_Applies_Duration()
    {
        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var parameters = new DialogParameters<SetFocusDurationDialog>
        {
            { x => x.TargetTitle, "Deep Work" },
            { x => x.InitialDuration, TimeSpan.FromMinutes(25) },
            { x => x.StartSession, true }
        };

        var dialogReference = await dialogService.ShowAsync<SetFocusDurationDialog>("Title", parameters);
        provider.Render();

        var textField = provider.FindComponent<MudTextField<string>>();
        textField.Instance.AutoFocus.Should().BeTrue();

        var input = provider.Find("input.mud-input-slot");
        input.Should().NotBeNull();
        input.GetAttribute("value").Should().Be("25m");

        // Verify quick presets are not present in custom time dialog
        provider.FindAll(".mud-chip").Should().BeEmpty();

        // Enter custom duration "45m"
        await provider.InvokeAsync(() => textField.Instance.ValueChanged.InvokeAsync("45m"));

        input = provider.Find("input.mud-input-slot");
        input.GetAttribute("value").Should().Be("45m");

        // Verify live alert feedback
        provider.Markup.Should().Contain("Time's up alert at:");
        provider.Markup.Should().Contain("45 min");

        // Submit
        var submitButton = provider.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Start session"));
        submitButton.Should().NotBeNull();
        submitButton?.GetAttribute("tabindex").Should().Be("2");
        if (submitButton is not null)
        {
            await submitButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        }

        var result = await dialogReference.Result;
        result.Should().NotBeNull();
        if (result is not null)
        {
            result.Canceled.Should().BeFalse();
            result.Data.Should().Be(TimeSpan.FromMinutes(45));
        }
    }

    [Fact]
    public async Task SetSessionTargetDialog_Renders_With_Autofocus_And_Applies_Target()
    {
        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var parameters = new DialogParameters<SetSessionTargetDialog>
        {
            { x => x.CurrentTarget, "Initial Project" }
        };

        var dialogReference = await dialogService.ShowAsync<SetSessionTargetDialog>("Title", parameters);
        provider.Render();

        var textField = provider.FindComponent<MudTextField<string>>();
        textField.Instance.AutoFocus.Should().BeTrue();

        var input = provider.Find("input.mud-input-slot");
        input.Should().NotBeNull();
        input.GetAttribute("value").Should().Be("Initial Project");

        await provider.InvokeAsync(() => textField.Instance.ValueChanged.InvokeAsync("New Objective"));

        var submitButton = provider.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Set target"));
        submitButton.Should().NotBeNull();
        submitButton?.GetAttribute("tabindex").Should().Be("2");
        if (submitButton is not null)
        {
            await submitButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        }

        var result = await dialogReference.Result;
        result.Should().NotBeNull();
        if (result is not null)
        {
            result.Canceled.Should().BeFalse();
            result.Data.Should().Be("New Objective");
        }
    }
}
