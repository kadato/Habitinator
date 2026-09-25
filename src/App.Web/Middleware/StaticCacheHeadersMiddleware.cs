namespace App.Web.Middleware;

public sealed class StaticCacheHeadersMiddleware(RequestDelegate next)
{
    private static readonly string[] ImmutablePrefixes =
    [
        "/_framework/",
        "/_content/",
        "/_next/",
        "/lib/",
        "/js/",
        "/css/",
        "/brand/",
        "/fonts/",
        "/icons/",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            var path = context.Request.Path.Value ?? string.Empty;
            context.Response.OnStarting(() =>
            {
                if (context.Response.Headers.ContainsKey("Cache-Control"))
                {
                    return Task.CompletedTask;
                }

                if (ImmutablePrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
                    return Task.CompletedTask;
                }

                if (IsDiscoveryFile(path))
                {
                    context.Response.Headers["Cache-Control"] = "public, max-age=3600";
                }

                return Task.CompletedTask;
            });
        }

        await next(context);
    }

    private static bool IsDiscoveryFile(string path) =>
        path.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/sitemap.xml", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/llms.txt", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/manifest.webmanifest", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/favicon.svg", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/favicon.png", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/apple-touch-icon.png", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/og-image.png", StringComparison.OrdinalIgnoreCase);
}
