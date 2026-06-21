using Bunit;

using FluentAssertions;

namespace App.Shared.RCL.Tests;

/// <summary>Smoke test for bUnit wiring; MudBlazor dialogs are covered via integration/E2E or manual QA.</summary>
public sealed class SampleEchoTests
{
    [Fact]
    public void Renders_ParameterText()
    {
        using var ctx = new BunitContext();
        var cut = ctx.Render<SampleEcho>(p => p.Add(x => x.Message, "hello-bunit"));
        cut.Markup.Should().Contain("hello-bunit");
        cut.Markup.Should().Contain("sample-echo");
    }
}
