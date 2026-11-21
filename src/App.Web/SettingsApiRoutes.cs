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
        var settingsApi = endpoints.MapGroup("/api/settings")
            .DisableAntiforgery()
            .RequireAuthorization("BoardOrJwt")
            .RequireRateLimiting("api");

        settingsApi.MapNotificationSettingsEndpoints();
        settingsApi.MapPreferencesSettingsEndpoints();
    }

    private static void MapNotificationSettingsEndpoints(this IEndpointRouteBuilder settingsApi)
    {
        settingsApi.MapGet("/notifications",
            async Task<Results<Ok<NotificationSettings>, NotFound>> (
                CurrentUserId user, ApplicationDbContext db, CancellationToken cancellationToken) =>
            {
                var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == user.Value, cancellationToken);
                if (row is null)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.Ok(row.NotificationSettings ?? NotificationSettings.CreateDefault());
            });

        settingsApi.MapPut("/notifications",
            async Task<Results<NoContent, NotFound>> (
                CurrentUserId user, ApplicationDbContext db, IBoardChangeNotifier boardChangeNotifier,
                NotificationSettings body, CancellationToken cancellationToken) =>
            {
                var row = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Value, cancellationToken);
                if (row is null)
                {
                    return TypedResults.NotFound();
                }

                row.NotificationSettings = body;
                await db.SaveChangesAsync(cancellationToken);
                await boardChangeNotifier.NotifyBoardChangedAsync(user.Value, cancellationToken);
                return TypedResults.NoContent();
            });
    }

    private static void MapPreferencesSettingsEndpoints(this IEndpointRouteBuilder settingsApi)
    {
        settingsApi.MapGet("/preferences",
            async Task<Results<Ok<UserPreferences>, NotFound>> (
                CurrentUserId user, ApplicationDbContext db, CancellationToken cancellationToken) =>
            {
                var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == user.Value, cancellationToken);
                if (row is null)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.Ok(row.UserPreferences ?? UserPreferences.CreateDefault());
            });

        settingsApi.MapPut("/preferences",
            async Task<Results<NoContent, NotFound>> (
                CurrentUserId user, ApplicationDbContext db, IBoardChangeNotifier boardChangeNotifier,
                UserPreferences body, CancellationToken cancellationToken) =>
            {
                var row = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Value, cancellationToken);
                if (row is null)
                {
                    return TypedResults.NotFound();
                }

                body.DisplayName = string.IsNullOrWhiteSpace(body.DisplayName)
                    ? null
                    : ZalgoSanitizer.Sanitize(body.DisplayName.Trim());
                row.UserPreferences = body;
                await db.SaveChangesAsync(cancellationToken);
                await boardChangeNotifier.NotifyBoardChangedAsync(user.Value, cancellationToken);
                return TypedResults.NoContent();
            });
    }
}
