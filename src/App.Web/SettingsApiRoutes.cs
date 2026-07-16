using System.Security.Claims;

using App.Shared.RCL;
using App.Shared.RCL.Models;
using App.Web.Auth;
using App.Web.Data;
using App.Web.Services;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace App.Web;

internal static class SettingsApiRoutes
{
    internal static void MapSettingsApi(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder settingsApi = endpoints.MapGroup("/api/settings")
            .DisableAntiforgery()
            .RequireAuthorization("BoardOrJwt")
            .RequireRateLimiting("api");

        settingsApi.MapNotificationSettingsEndpoints();
        settingsApi.MapPreferencesSettingsEndpoints();
    }

    private static void MapNotificationSettingsEndpoints(this IEndpointRouteBuilder settingsApi)
    {
        settingsApi.MapGet("/notifications",
            async Task<Results<Ok<NotificationSettings>, UnauthorizedHttpResult, NotFound>> (
                ClaimsPrincipal user, ApplicationDbContext db, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return TypedResults.Unauthorized();
                }

                ApplicationUser? row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (row is null)
                {
                    return TypedResults.NotFound();
                }

                NotificationSettings settings = row.NotificationSettings ?? NotificationSettings.CreateDefault();
                return TypedResults.Ok(settings);
            });

        settingsApi.MapPut("/notifications",
            async Task<Results<NoContent, UnauthorizedHttpResult, NotFound>> (
                ClaimsPrincipal user, ApplicationDbContext db, IBoardChangeNotifier boardChangeNotifier,
                NotificationSettings body, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return TypedResults.Unauthorized();
                }

                ApplicationUser? row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (row is null)
                {
                    return TypedResults.NotFound();
                }

                row.NotificationSettings = body;
                await db.SaveChangesAsync(cancellationToken);
                await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
                return TypedResults.NoContent();
            });
    }

    private static void MapPreferencesSettingsEndpoints(this IEndpointRouteBuilder settingsApi)
    {
        settingsApi.MapGet("/preferences",
            async Task<Results<Ok<UserPreferences>, UnauthorizedHttpResult, NotFound>> (
                ClaimsPrincipal user, ApplicationDbContext db, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return TypedResults.Unauthorized();
                }

                ApplicationUser? row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (row is null)
                {
                    return TypedResults.NotFound();
                }

                UserPreferences settings = row.UserPreferences ?? UserPreferences.CreateDefault();
                return TypedResults.Ok(settings);
            });

        settingsApi.MapPut("/preferences",
            async Task<Results<NoContent, UnauthorizedHttpResult, NotFound>> (
                ClaimsPrincipal user, ApplicationDbContext db, IBoardChangeNotifier boardChangeNotifier,
                UserPreferences body, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return TypedResults.Unauthorized();
                }

                ApplicationUser? row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (row is null)
                {
                    return TypedResults.NotFound();
                }

                // Sanitize free-text field before persisting
                body.DisplayName = string.IsNullOrWhiteSpace(body.DisplayName)
                    ? null
                    : ZalgoSanitizer.Sanitize(body.DisplayName.Trim());

                row.UserPreferences = body;
                await db.SaveChangesAsync(cancellationToken);
                await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
                return TypedResults.NoContent();
            });
    }
}
