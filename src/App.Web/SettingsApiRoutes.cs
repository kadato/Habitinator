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

    private static void MapNotificationSettingsEndpoints(this IEndpointRouteBuilder settingsApi) =>
        settingsApi.MapSettingsEndpoints(
            "notifications",
            static row => row.NotificationSettings,
            static () => NotificationSettings.CreateDefault(),
            static (row, value) => row.NotificationSettings = value);

    private static void MapPreferencesSettingsEndpoints(this IEndpointRouteBuilder settingsApi) =>
        settingsApi.MapSettingsEndpoints(
            "preferences",
            static row => row.UserPreferences,
            static () => UserPreferences.CreateDefault(),
            static (row, value) => row.UserPreferences = value,
            static value =>
            {
                value.DisplayName = string.IsNullOrWhiteSpace(value.DisplayName)
                    ? null
                    : ZalgoSanitizer.Sanitize(value.DisplayName.Trim());
            });

    private static void MapSettingsEndpoints<T>(
        this IEndpointRouteBuilder settingsApi,
        string segment,
        Func<ApplicationUser, T?> getter,
        Func<T> defaultValue,
        Action<ApplicationUser, T> setter,
        Action<T>? sanitize = null)
    {
        settingsApi.MapGet("/" + segment,
            async Task<Results<Ok<T>, NotFound>> (
                CurrentUserId user, ApplicationDbContext db, CancellationToken cancellationToken) =>
            {
                var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == user.Value, cancellationToken);
                if (row is null)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.Ok(getter(row) ?? defaultValue());
            });

        settingsApi.MapPut("/" + segment,
            async Task<Results<NoContent, NotFound>> (
                CurrentUserId user, ApplicationDbContext db, IBoardChangeNotifier boardChangeNotifier,
                T body, CancellationToken cancellationToken) =>
            {
                var row = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Value, cancellationToken);
                if (row is null)
                {
                    return TypedResults.NotFound();
                }

                sanitize?.Invoke(body);
                setter(row, body);
                await db.SaveChangesAsync(cancellationToken);
                await boardChangeNotifier.NotifyBoardChangedAsync(user.Value, cancellationToken);
                return TypedResults.NoContent();
            });
    }
}
