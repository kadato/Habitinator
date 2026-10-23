namespace App.Shared.RCL.Services;

/// <summary>Ready to copy MCP client configs for the deployed Habitinator MCP server.</summary>
public static class McpClientConfigSnippets
{
    public const string FallbackEndpoint = "https://habitinator.app/mcp";
    public const string TokenPlaceholder = "YOUR_MCP_TOKEN";

    public static string Claude(string? endpoint, string? token)
    {
        var url = string.IsNullOrWhiteSpace(endpoint) ? FallbackEndpoint : endpoint;
        var bearer = string.IsNullOrWhiteSpace(token) ? TokenPlaceholder : token;
        return $$"""
        {
          "mcpServers": {
            "habitinator": {
              "url": "{{url}}",
              "headers": {
                "Authorization": "Bearer {{bearer}}"
              }
            }
          }
        }
        """;
    }

    public static string OpenCode(string? endpoint, string? token)
    {
        var url = string.IsNullOrWhiteSpace(endpoint) ? FallbackEndpoint : endpoint;
        var bearer = string.IsNullOrWhiteSpace(token) ? TokenPlaceholder : token;
        return $$"""
        {
          "$schema": "https://opencode.ai/config.json",
          "mcp": {
            "servers": {
              "habitinator": {
                "type": "remote",
                "url": "{{url}}",
                "oauth": false,
                "headers": {
                  "Authorization": "Bearer {{bearer}}"
                }
              }
            }
          }
        }
        """;
    }

    public static string OpenCodeCli(string? endpoint, string? token)
    {
        var url = string.IsNullOrWhiteSpace(endpoint) ? FallbackEndpoint : endpoint;
        return string.IsNullOrWhiteSpace(token)
            ? $"export HABITINATOR_MCP_TOKEN={TokenPlaceholder}\nopencode mcp add habitinator --url {url} --header 'Authorization=Bearer {{env:HABITINATOR_MCP_TOKEN}}'"
            : $"opencode mcp add habitinator --url {url} --header 'Authorization=Bearer {token}'";
    }

    public static string Hermes(string? endpoint, string? token)
    {
        var url = string.IsNullOrWhiteSpace(endpoint) ? FallbackEndpoint : endpoint;
        var bearer = string.IsNullOrWhiteSpace(token) ? TokenPlaceholder : token;
        return $$"""
        mcp_servers:
          habitinator:
            url: "{{url}}"
            headers:
              Authorization: "Bearer {{bearer}}"
            timeout: 120
            connect_timeout: 60
            enabled: true
        """;
    }

    public static string Curl(string? endpoint, string? token)
    {
        var url = string.IsNullOrWhiteSpace(endpoint) ? FallbackEndpoint : endpoint;
        var bearer = string.IsNullOrWhiteSpace(token) ? TokenPlaceholder : token;
        return "curl -s -X POST " + url + " \\\n" +
            "  -H \"Authorization: Bearer " + bearer + "\" \\\n" +
            "  -H 'Content-Type: application/json' \\\n" +
            "  -H 'Accept: application/json, text/event-stream' \\\n" +
            "  -d '{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}'";
    }
}
