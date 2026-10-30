using System.Reflection;

using App.Shared.RCL.Models;

using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

namespace App.Web.Mcp;

/// <summary>Builds the public MCP server card at /.well-known/mcp/server-card.json.</summary>
internal static class McpServerCard
{
    public static object Build(HttpContext http, IOptions<SitePublicOptions> siteOptions)
    {
        _ = http;
        var site = siteOptions.Value;
        var baseUrl = site.PublicBaseUrl.OriginalString.TrimEnd('/');
        var version = typeof(McpRegistration).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var endpoint = $"{baseUrl}/mcp";
        var cardUrl = $"{baseUrl}/.well-known/mcp/server-card.json";

        return new Dictionary<string, object?>
        {
            ["name"] = "habitinator",
            ["title"] = "Habitinator board helper",
            ["version"] = version,
            ["description"] = "Authenticated Streamable HTTP MCP server for Habitinator boards: habits, dailies, todos, activity, focus timer, and settings. Supports seamless OAuth 2.1 (RFC 9728 / RFC 8414) and personal tokens.",
            ["vendor"] = new Dictionary<string, object?> { ["name"] = "Habitinator", ["url"] = site.RepositoryUrl.OriginalString },
            ["license"] = new Dictionary<string, object?> { ["code"] = "MIT", ["repo"] = site.RepositoryUrl.OriginalString },
            ["endpoint"] = endpoint,
            ["transport"] = "streamable-http",
            ["stateless"] = true,
            ["authentication"] = new Dictionary<string, object?>
            {
                ["type"] = "oauth2",
                ["authorizationServer"] = baseUrl,
                ["authorizationEndpoint"] = $"{baseUrl}/oauth/authorize",
                ["tokenEndpoint"] = $"{baseUrl}/oauth/token",
                ["registrationEndpoint"] = $"{baseUrl}/oauth/register",
                ["protectedResourceMetadata"] = $"{baseUrl}/.well-known/oauth-protected-resource",
                ["scopes"] = new[] { McpTokenScopes.Full, McpTokenScopes.TrackingOnly, McpTokenScopes.ReadOnly },
                ["personalTokenEndpoint"] = $"{baseUrl}/api/account/mcp-token",
                ["docs"] = $"{baseUrl}/auth.md",
            },
            ["capabilities"] = new Dictionary<string, object?> { ["tools"] = true, ["prompts"] = true, ["resources"] = true, ["completions"] = true, ["logging"] = true },
            ["tools"] = ListTools(),
            ["prompts"] = ListPrompts(),
            ["links"] = new Dictionary<string, object?>
            {
                ["site"] = baseUrl,
                ["serverCard"] = cardUrl,
                ["spec"] = site.RepositoryUrl.OriginalString.TrimEnd('/') + "/blob/main/docs/mcp.md",
                ["skill"] = $"{baseUrl}/.well-known/agent-skills/habitinator/SKILL.md",
                ["auth"] = $"{baseUrl}/auth.md",
            },
        };
    }

    internal static IReadOnlyList<object> ListTools()
    {
        var asm = typeof(McpRegistration).Assembly;
        var found = new List<object>();
        foreach (var type in asm.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
            {
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr?.Name is null)
                {
                    continue;
                }

                found.Add(new Dictionary<string, object?> { ["name"] = attr.Name, ["title"] = attr.Title ?? attr.Name });
            }
        }

        return found
            .OrderBy(t => ((Dictionary<string, object?>)t)["name"] as string, StringComparer.Ordinal)
            .ToList();
    }

    internal static IReadOnlyList<object> ListPrompts()
    {
        var asm = typeof(McpRegistration).Assembly;
        var found = new List<object>();
        foreach (var type in asm.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerPromptTypeAttribute>() is null)
            {
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerPromptAttribute>();
                if (attr?.Name is null)
                {
                    continue;
                }

                found.Add(new Dictionary<string, object?> { ["name"] = attr.Name, ["title"] = attr.Title ?? attr.Name });
            }
        }

        return found
            .OrderBy(p => ((Dictionary<string, object?>)p)["name"] as string, StringComparer.Ordinal)
            .ToList();
    }
}
