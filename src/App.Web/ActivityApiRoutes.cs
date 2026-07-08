using System.Security.Claims;

using App.Shared.RCL;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Auth;
using App.Web.Services;

using Microsoft.AspNetCore.Builder;

namespace App.Web;

internal static class ActivityApiRoutes
{
    internal static void MapActivityApi(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder activityApi = endpoints.MapGroup("/api/activity")
            .DisableAntiforgery()
            .RequireAuthorization("BoardOrJwt")
            .RequireRateLimiting("api");

        activityApi.MapGet("dashboard",
            async (ClaimsPrincipal user, ActivityStatisticsService stats, string? period, string? tag,
                CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(await stats.GetDashboardAsync(userId, period, tag, cancellationToken));
            });

        activityApi.MapGet("daily-contributions",
            async (ClaimsPrincipal user, ActivityStatisticsService stats, string? period, string? tag,
                CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(await stats.GetDailyContributionsAsync(userId, period, tag, cancellationToken));
            });

        activityApi.MapGet("day",
            async (ClaimsPrincipal user, ActivityStatisticsService stats, DateOnly date, string? tag,
                CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(await stats.GetActivityDayDetailAsync(userId, date, tag, cancellationToken));
            });

        activityApi.MapPost("log", async (
            ClaimsPrincipal user,
            BoardPersistenceService persistence,
            DemoUserResolver demoUserResolver,
            ActivityLogRequest body,
            CancellationToken cancellationToken) =>
        {
            if (AuthenticatedUserId.TryGet(user) is null)
            {
                return Results.Unauthorized();
            }

            if (body.DurationSeconds.HasValue && (body.DurationSeconds.Value < 0 || body.DurationSeconds.Value > 86400))
            {
                return Results.BadRequest(new { detail = "Duration must be between 0 and 86,400 seconds (24 hours)." });
            }

            Guid resolvedUserId = await demoUserResolver.ResolveUserIdAsync(user, cancellationToken);

            if (body.EventType == ActivityEventType.TimerSession && body.DurationSeconds.HasValue)
            {
                await persistence.LogTimerSessionAsync(
                    resolvedUserId,
                    TimeSpan.FromSeconds(body.DurationSeconds.Value),
                    body.BoardItemId,
                    body.CustomLabel,
                    cancellationToken);
            }
            else
            {
                await persistence.LogActivityAsync(
                    resolvedUserId,
                    body.EventType,
                    body.BoardItemId,
                    body.DurationSeconds,
                    body.CustomLabel,
                    cancellationToken);
            }
            return Results.NoContent();
        });
    }
}
