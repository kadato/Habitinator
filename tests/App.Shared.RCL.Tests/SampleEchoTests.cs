using Bunit;

namespace App.Shared.RCL.Tests;

/// <summary>Smoke test for bUnit wiring; MudBlazor dialogs are covered via integration/E2E or manual QA.</summary>
public sealed class SampleEchoTests
{
    [Fact]
    public void Renders_ParameterText()
    {
        using var ctx = new TestContext();
        var cut = ctx.RenderComponent<SampleEcho>(p => p.Add(x => x.Message, "hello-bunit"));
        Assert.Contains("hello-bunit", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("sample-echo", cut.Markup, StringComparison.Ordinal);
    }
}
