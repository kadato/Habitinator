namespace App.Web.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> ImmutablePathPrefixes =
    [
        "/_framework/",
        "/_content/"
    ];

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
        headers.XFrameOptions = "DENY";
#pragma warning disable S7039 // Suppress Content Security Policies restriction warning for Blazor Server compatibility
        headers.ContentSecurityPolicy =
            "default-src 'self'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'; " +
            "img-src 'self' data: blob:; " +
            "font-src 'self' data: https://fonts.gstatic.com; " +
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
            "script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval'; " +
            "connect-src 'self' https: wss: ws:; " +
            "object-src 'none'; " +
            "upgrade-insecure-requests";
#pragma warning restore S7039

        // Set aggressive caching for fingerprinted framework assets. They are immutable, so cache for 1 year.
        if (IsImmutableAsset(context.Request.Path))
        {
            headers.CacheControl = "public, max-age=31536000, immutable";
        }

        return next(context);
    }

    private static bool IsImmutableAsset(PathString path)
    {
        var pathValue = path.Value;
        if (string.IsNullOrEmpty(pathValue))
        {
            return false;
        }

        return ImmutablePathPrefixes.Any(prefix =>
            pathValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
