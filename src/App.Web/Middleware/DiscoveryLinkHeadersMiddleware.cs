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
                var baseUrl = _site.PublicBaseUrl.TrimEnd('/');
                context.Response.Headers.Append(
                    "Link",
                    $"<{baseUrl}/openapi/v1.json>; rel=\"openapi\"; type=\"application/vnd.oai.openapi+json\"");
                context.Response.Headers.Append(
                    "Link",
                    $"<{baseUrl}/.well-known/api-catalog>; rel=\"api-catalog\"; type=\"application/linkset+json\"");
                context.Response.Headers.Append("Link", $"<{baseUrl}/sitemap.xml>; rel=\"sitemap\"; type=\"application/xml\"");
                context.Response.Headers.Append("Link", $"<{baseUrl}/llms.txt>; rel=\"alternate\"; type=\"text/plain\"; title=\"LLM site index\"");
            }
        }

        return next(context);
    }
}
