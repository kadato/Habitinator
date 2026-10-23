using App.Shared.RCL.Models;

using Microsoft.Extensions.Options;

namespace App.Web.Middleware;

/// <summary>Advertises machine-readable discovery resources per Website Specification agent-readiness guidance.</summary>
public sealed class DiscoveryLinkHeadersMiddleware(RequestDelegate next, IOptions<SitePublicOptions> siteOptions)
{
    private readonly SitePublicOptions _site = siteOptions.Value;

    public Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            var accept = context.Request.Headers.Accept.ToString();
            if (accept.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(accept))
            {
                var baseUrl = _site.PublicBaseUrl.OriginalString.TrimEnd('/');
                context.Response.Headers.Append(
                    "Link",
                    $"<{baseUrl}/openapi/v1.json>; rel=\"openapi\"; type=\"application/vnd.oai.openapi+json\"");
                context.Response.Headers.Append(
                    "Link",
                    $"<{baseUrl}/.well-known/api-catalog>; rel=\"api-catalog\"; type=\"application/linkset+json\"");
                context.Response.Headers.Append("Link", $"<{baseUrl}/sitemap.xml>; rel=\"sitemap\"; type=\"application/xml\"");
                context.Response.Headers.Append("Link", $"<{baseUrl}/llms.txt>; rel=\"describedby\"; type=\"text/markdown\"; title=\"LLM site index\"");
                context.Response.Headers.Append("Link", $"<{baseUrl}/.well-known/mcp/server-card.json>; rel=\"mcp\"; type=\"application/json\"; title=\"Habitinator MCP server card\"");
                context.Response.Headers.Append("Link", $"<{baseUrl}/.well-known/ai-catalog.json>; rel=\"ai-catalog\"; type=\"application/json\"; title=\"Agent capability catalog\"");
                context.Response.Headers.Append("Link", $"<{baseUrl}/.well-known/agent-skills/index.json>; rel=\"agent-skills\"; type=\"application/json\"; title=\"Agent skills index\"");
            }
        }

        return next(context);
    }
}
