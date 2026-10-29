namespace App.Shared.RCL.Services;

/// <summary>Ready to copy MCP client configs for the deployed Habitinator MCP server.</summary>
public static class McpClientConfigSnippets
{
    public const string FallbackEndpoint = "https://habitinator.app/mcp";
    public const string TokenPlaceholder = "YOUR_MCP_TOKEN";
    public const string EnvVarName = "HABITINATOR_MCP_TOKEN";

    public static string ExportEnv(string? token)
    {
        var bearer = string.IsNullOrWhiteSpace(token) ? TokenPlaceholder : token;
        return $"export {EnvVarName}=\"{bearer}\"";
    }

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

    public static string ClaudeCodeCli(string? endpoint, string? token)
    {
        var url = string.IsNullOrWhiteSpace(endpoint) ? FallbackEndpoint : endpoint;
        var bearer = string.IsNullOrWhiteSpace(token) ? $"${EnvVarName}" : token;
        return $"claude mcp add habitinator {url} --header \"Authorization=Bearer {bearer}\"";
    }

    public static string OpenCode(string? endpoint, string? token, bool useEnvVar = false)
    {
        var url = string.IsNullOrWhiteSpace(endpoint) ? FallbackEndpoint : endpoint;
        var bearer = ResolveBearer(token, useEnvVar, $"{{env:{EnvVarName}}}");
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

    public static string OpenCodeCli(string? endpoint, string? token, bool useEnvVar = false)
    {
        var url = string.IsNullOrWhiteSpace(endpoint) ? FallbackEndpoint : endpoint;
        var bearer = useEnvVar || string.IsNullOrWhiteSpace(token)
            ? $"{{env:{EnvVarName}}}"
            : token;
        return $"opencode mcp add habitinator --url {url} --header 'Authorization=Bearer {bearer}'";
    }

    public static string Hermes(string? endpoint, string? token, bool useEnvVar = false)
    {
        var url = string.IsNullOrWhiteSpace(endpoint) ? FallbackEndpoint : endpoint;
        var bearer = ResolveBearer(token, useEnvVar, $"${{{EnvVarName}}}");
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

    private static string ResolveBearer(string? token, bool useEnvVar, string envPattern)
    {
        if (useEnvVar)
        {
            return envPattern;
        }

        return string.IsNullOrWhiteSpace(token) ? TokenPlaceholder : token;
    }

    public static string HermesAppend(string? endpoint, string? token, bool useEnvVar = false)
    {
        var url = string.IsNullOrWhiteSpace(endpoint) ? FallbackEndpoint : endpoint;
        var bearer = useEnvVar || string.IsNullOrWhiteSpace(token)
            ? $"${{{EnvVarName}}}"
            : token;
        return "cat << 'EOF' >> ~/.hermes/config.yaml\n" +
            "\n" +
            "mcp_servers:\n" +
            "  habitinator:\n" +
            $"    url: \"{url}\"\n" +
            "    headers:\n" +
            $"      Authorization: \"Bearer {bearer}\"\n" +
            "    timeout: 120\n" +
            "    connect_timeout: 60\n" +
            "    enabled: true\n" +
            "EOF";
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

    public static string HealthCheckCurl(string? endpoint, string? token)
    {
        var url = string.IsNullOrWhiteSpace(endpoint) ? FallbackEndpoint : endpoint;
        var bearer = string.IsNullOrWhiteSpace(token) ? $"${EnvVarName}" : token;
        return $"curl -s -X POST {url} -H \"Authorization: Bearer {bearer}\" -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' -d '{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{{}}}}' | jq '.result.tools | length'";
    }
}
