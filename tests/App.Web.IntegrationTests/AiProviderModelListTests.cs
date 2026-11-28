using App.Web.Services;

using FluentAssertions;

namespace App.Web.IntegrationTests;

public sealed class AiProviderModelListTests
{
    [Theory]
    [InlineData("https://opencode.ai/inference/go/openai/v1", "https://opencode.ai/inference/go/openai/v1/models")]
    [InlineData("https://opencode.ai/inference/go/openai/v1/chat/completions", "https://opencode.ai/inference/go/openai/v1/models")]
    [InlineData("https://api.openai.com/v1/responses/", "https://api.openai.com/v1/models")]
    [InlineData("https://api.openai.com/v1/models", "https://api.openai.com/v1/models")]
    public void BuildModelsUrl_lands_on_the_models_path(string endpoint, string expected)
    {
        AiProviderModelList.BuildModelsUrl(endpoint).Should().Be(expected);
    }

    [Fact]
    public void Parse_reads_openai_compatible_payloads()
    {
        var ids = AiProviderModelList.Parse(
            """{"object":"list","data":[{"id":"gpt-4o","owned_by":"openai"},{"id":"gpt-4o-mini"}]}""");

        ids.Should().Equal("gpt-4o", "gpt-4o-mini");
    }

    [Fact]
    public void Parse_reads_ollama_and_bare_arrays()
    {
        AiProviderModelList.Parse("""{"models":[{"name":"llama3.2:latest","model":"llama3.2:latest"}]}""")
            .Should().Equal("llama3.2:latest");
        AiProviderModelList.Parse("""["mimo-v2.5",{"model":"qwen3"}]""")
            .Should().Equal("mimo-v2.5", "qwen3");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"data":[]}""")]
    public void Parse_returns_empty_for_unusable_payloads(string body)
    {
        AiProviderModelList.Parse(body).Should().BeEmpty();
    }
}
