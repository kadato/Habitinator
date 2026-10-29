using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class McpClientConfigSnippetsTests
{
    private const string Endpoint = "https://habitinator.app/mcp";
    private const string Token = "test-token-123";

    [Fact]
    public void ExportEnv_ContainsEnvVarAndToken()
    {
        var snippet = McpClientConfigSnippets.ExportEnv(Token);
        snippet.Should().Be($"export HABITINATOR_MCP_TOKEN=\"{Token}\"");

        var placeholderSnippet = McpClientConfigSnippets.ExportEnv(null);
        placeholderSnippet.Should().Be($"export HABITINATOR_MCP_TOKEN=\"{McpClientConfigSnippets.TokenPlaceholder}\"");
    }

    [Fact]
    public void Claude_ContainsEndpointAndToken()
    {
        var snippet = McpClientConfigSnippets.Claude(Endpoint, Token);
        snippet.Should().Contain(Endpoint);
        snippet.Should().Contain($"Bearer {Token}");
        snippet.Should().Contain("mcpServers");
    }

    [Fact]
    public void ClaudeCodeCli_FormatsCommandLine()
    {
        var snippet = McpClientConfigSnippets.ClaudeCodeCli(Endpoint, Token);
        snippet.Should().Be($"claude mcp add habitinator {Endpoint} --header \"Authorization=Bearer {Token}\"");

        var envSnippet = McpClientConfigSnippets.ClaudeCodeCli(null, null);
        envSnippet.Should().Be($"claude mcp add habitinator {McpClientConfigSnippets.FallbackEndpoint} --header \"Authorization=Bearer $HABITINATOR_MCP_TOKEN\"");
    }

    [Fact]
    public void OpenCode_UsesRemoteTypeAndDisablesOAuth()
    {
        var snippet = McpClientConfigSnippets.OpenCode(Endpoint, Token);
        snippet.Should().Contain("\"type\": \"remote\"");
        snippet.Should().Contain("\"oauth\": false");
        snippet.Should().Contain(Endpoint);
        snippet.Should().Contain($"Bearer {Token}");

        var envSnippet = McpClientConfigSnippets.OpenCode(Endpoint, Token, useEnvVar: true);
        envSnippet.Should().Contain("Bearer {env:HABITINATOR_MCP_TOKEN}");
    }

    [Fact]
    public void Hermes_UsesYamlShape()
    {
        var snippet = McpClientConfigSnippets.Hermes(Endpoint, Token);
        snippet.Should().Contain("mcp_servers:");
        snippet.Should().Contain(Endpoint);
        snippet.Should().Contain($"Bearer {Token}");

        var envSnippet = McpClientConfigSnippets.Hermes(Endpoint, Token, useEnvVar: true);
        envSnippet.Should().Contain("Bearer ${HABITINATOR_MCP_TOKEN}");
    }

    [Fact]
    public void HermesAppend_GeneratesHeredocScript()
    {
        var snippet = McpClientConfigSnippets.HermesAppend(Endpoint, Token);
        snippet.Should().Contain("cat << 'EOF' >> ~/.hermes/config.yaml");
        snippet.Should().Contain($"Authorization: \"Bearer {Token}\"");
        snippet.Should().Contain("timeout: 120");

        var envSnippet = McpClientConfigSnippets.HermesAppend(null, null, useEnvVar: true);
        envSnippet.Should().Contain("Authorization: \"Bearer ${HABITINATOR_MCP_TOKEN}\"");
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

        var tokenSnippet = McpClientConfigSnippets.OpenCodeCli(Endpoint, Token);
        tokenSnippet.Should().Contain($"Authorization=Bearer {Token}");
    }

    [Fact]
    public void HealthCheckCurl_GeneratesTerminalCommand()
    {
        var snippet = McpClientConfigSnippets.HealthCheckCurl(Endpoint, Token);
        snippet.Should().Contain(Endpoint);
        snippet.Should().Contain($"Bearer {Token}");
        snippet.Should().Contain("jq '.result.tools | length'");

        var envSnippet = McpClientConfigSnippets.HealthCheckCurl(null, null);
        envSnippet.Should().Contain("Bearer $HABITINATOR_MCP_TOKEN");
    }
}
