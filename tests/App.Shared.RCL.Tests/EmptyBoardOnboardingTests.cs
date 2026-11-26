#pragma warning disable MUD0012

using App.Shared.RCL.Components;

using Bunit;

using FluentAssertions;

using Microsoft.AspNetCore.Components;

using MudBlazor.Services;

namespace App.Shared.RCL.Tests;

public sealed class EmptyBoardOnboardingTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();

    public EmptyBoardOnboardingTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void Renders_template_choices_and_explains_the_model()
    {
        var cut = _ctx.Render<EmptyBoardOnboarding>();

        cut.Markup.Should().Contain("Habits count, dailies repeat, to-dos end.");
        cut.Markup.Should().Contain("Morning routine");
        cut.Markup.Should().Contain("Fitness");
        cut.Markup.Should().Contain("Focus");
    }

    [Fact]
    public void Template_click_raises_the_template_key()
    {
        string? templateKey = null;
        var cut = _ctx.Render<EmptyBoardOnboarding>(parameters => parameters
            .Add(p => p.OnUseTemplate, EventCallback.Factory.Create<string>(this, key => templateKey = key)));

        cut.FindAll("button")
            .First(b => b.TextContent.Contains("Fitness", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => templateKey.Should().Be("fitness"));
    }
}
