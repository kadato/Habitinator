using App.Web.Auth;
using App.Web.Data;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Middleware;

/// <summary>
/// Rejects API and hub requests from an authenticated identity whose user row no longer exists.
/// Identity cookies and login JWTs can outlive the row they point at, for example after a
/// database reset. Without this check, reads return 404, and writes fail later as a foreign key
/// violation and surface as a 500 instead of a sign-in prompt.
/// </summary>
public sealed class ExistingUserMiddleware(RequestDelegate next, ILogger<ExistingUserMiddleware> logger)
{
    /// <summary>
    /// Endpoints that start or end a session must work even when the current cookie points
    /// at a removed row, or the user could not sign in or sign out to recover.
    /// </summary>
    private static readonly PathString[] SessionPaths =
    [
        "/api/auth/register",
        "/api/auth/register-form",
        "/api/auth/forgot-password",
        "/api/auth/reset-password",
        "/api/auth/login",
        "/api/auth/guest-jwt",
        "/api/auth/guest-login",
        "/api/auth/cookie-login",
        "/api/auth/cookie-logout",
    ];

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        if (!IsGuardedPath(context.Request.Path)
            || context.User.Identity?.IsAuthenticated != true
            || await UserExistsAsync(context, db))
        {
            await next(context);
            return;
        }

        logger.LogInformation(
            "Rejected {Method} {Path}: authenticated user {UserId} has no database row.",
            context.Request.Method,
            context.Request.Path,
            AuthenticatedUserId.TryGet(context.User));

        if (context.User.Identities.Any(static identity =>
                identity.IsAuthenticated && identity.AuthenticationType == IdentityConstants.ApplicationScheme))
        {
            // Clear the stale cookie so the next request starts a fresh sign-in.
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(
            new { detail = "Your session points at an account that no longer exists. Sign in again." },
            context.RequestAborted);
    }

    private static bool IsGuardedPath(PathString path) =>
        !SessionPaths.Any(candidate => path.StartsWithSegments(candidate, StringComparison.OrdinalIgnoreCase))
        && (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase));

    private static async Task<bool> UserExistsAsync(HttpContext context, ApplicationDbContext db)
    {
        var userId = AuthenticatedUserId.TryGet(context.User);
        return userId is { } id
               && await db.Users.AsNoTracking().AnyAsync(u => u.Id == id, context.RequestAborted);
    }
}
