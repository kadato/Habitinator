namespace App.Web.Middleware;

public sealed class SameOriginCsrfMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    private static readonly PathString[] CookiePostPaths =
    [
        "/api/auth/cookie-login",
        "/api/auth/guest-login",
        "/api/auth/register-form",
        "/api/auth/cookie-logout",
        "/api/account/change-password",
        "/api/account/delete",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method)
            && IsCookiePost(context)
            && !context.Request.Headers.ContainsKey("Authorization")
            && !HasValidOrigin(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { detail = "Cross-site request rejected." }, context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool IsCookiePost(HttpContext context)
    {
        var path = context.Request.Path;
        return CookiePostPaths.Any(candidate => path.StartsWithSegments(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasValidOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin))
        {
            return IsSameHost(context, origin);
        }

        var referer = context.Request.Headers.Referer.ToString();
        if (!string.IsNullOrWhiteSpace(referer))
        {
            return IsSameHost(context, referer);
        }

        return environment.IsDevelopment() || environment.IsEnvironment(AppEnvironment.Testing);
    }

    private static bool IsSameHost(HttpContext context, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase);
    }
}
