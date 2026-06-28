using System.Security.Claims;

using App.Shared.RCL;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Auth;
using App.Web.Data;
using App.Web.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
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
            async (ClaimsPrincipal user, ApplicationDbContext db, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                ApplicationUser? row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (row is null)
                {
                    return Results.NotFound();
                }

                NotificationSettings settings = NotificationSettingsJson.DeserializeOrDefault(row.NotificationSettingsJson);
                return Results.Ok(settings);
            });

        settingsApi.MapPut("/notifications",
            async (ClaimsPrincipal user, ApplicationDbContext db, IBoardChangeNotifier boardChangeNotifier,
                NotificationSettings body, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                ApplicationUser? row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (row is null)
                {
                    return Results.NotFound();
                }

                row.NotificationSettingsJson = NotificationSettingsJson.Serialize(body);
                await db.SaveChangesAsync(cancellationToken);
                await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
                return Results.NoContent();
            });
    }

    private static void MapPreferencesSettingsEndpoints(this IEndpointRouteBuilder settingsApi)
    {
        settingsApi.MapGet("/preferences",
            async (ClaimsPrincipal user, ApplicationDbContext db, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                ApplicationUser? row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (row is null)
                {
                    return Results.NotFound();
                }

                UserPreferences settings = UserPreferencesJson.DeserializeOrDefault(row.UserPreferencesJson);
                return Results.Ok(settings);
            });

        settingsApi.MapPut("/preferences",
            async (ClaimsPrincipal user, ApplicationDbContext db, IBoardChangeNotifier boardChangeNotifier,
                UserPreferences body, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                ApplicationUser? row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (row is null)
                {
                    return Results.NotFound();
                }

                // Sanitize free-text field before persisting
                body.DisplayName = string.IsNullOrWhiteSpace(body.DisplayName)
                    ? null
                    : ZalgoSanitizer.Sanitize(body.DisplayName.Trim());

                row.UserPreferencesJson = UserPreferencesJson.Serialize(body);
                await db.SaveChangesAsync(cancellationToken);
                await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
                return Results.NoContent();
            });
    }
}
