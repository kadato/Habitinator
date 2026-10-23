using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class McpClientConfigSnippetsTests
{
    private const string Endpoint = "https://habitinator.app/mcp";
    private const string Token = "test-token-123";

    [Fact]
    public void Claude_ContainsEndpointAndToken()
    {
        var snippet = McpClientConfigSnippets.Claude(Endpoint, Token);
        snippet.Should().Contain(Endpoint);
        snippet.Should().Contain($"Bearer {Token}");
        snippet.Should().Contain("mcpServers");
    }

    [Fact]
    public void OpenCode_UsesRemoteTypeAndDisablesOAuth()
    {
        var snippet = McpClientConfigSnippets.OpenCode(Endpoint, Token);
        snippet.Should().Contain("\"type\": \"remote\"");
        snippet.Should().Contain("\"oauth\": false");
        snippet.Should().Contain(Endpoint);
        snippet.Should().Contain($"Bearer {Token}");
    }

    [Fact]
    public void Hermes_UsesYamlShape()
    {
        var snippet = McpClientConfigSnippets.Hermes(Endpoint, Token);
        snippet.Should().Contain("mcp_servers:");
        snippet.Should().Contain(Endpoint);
        snippet.Should().Contain($"Bearer {Token}");
    }

    [Fact]
    public void Snippets_FallBackToPlaceholder_WhenMissing()
    {
        McpClientConfigSnippets.Claude(null, null).Should().Contain(McpClientConfigSnippets.TokenPlaceholder);
        McpClientConfigSnippets.OpenCode(null, null).Should().Contain(McpClientConfigSnippets.FallbackEndpoint);
        McpClientConfigSnippets.Hermes(null, null).Should().Contain(McpClientConfigSnippets.TokenPlaceholder);
        McpClientConfigSnippets.Curl(Endpoint, Token).Should().Contain("Accept: application/json, text/event-stream");
    }

    [Fact]
    public void OpenCodeCli_UsesEnvPlaceholder_WhenTokenMissing()
    {
        var snippet = McpClientConfigSnippets.OpenCodeCli(Endpoint, null);
        snippet.Should().Contain("opencode mcp add habitinator");
        snippet.Should().Contain("{env:HABITINATOR_MCP_TOKEN}");
    }
}
