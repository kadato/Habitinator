using System.Security.Claims;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Remote;
using App.Web.Auth;
using App.Web.Services;

namespace App.Web;

internal static class ActivityApiRoutes
{
    internal static void MapActivityApi(this IEndpointRouteBuilder endpoints)
    {
        var activityApi = endpoints.MapGroup("/api/activity")
            .DisableAntiforgery()
            .RequireAuthorization("BoardOrJwt")
            .RequireRateLimiting("api");

        activityApi.MapGet("dashboard", GetDashboardAsync);
        activityApi.MapGet("daily-contributions", GetDailyContributionsAsync);
        activityApi.MapGet("habit-contributions", GetHabitContributionsAsync);
        activityApi.MapGet("day", GetActivityDayDetailAsync);
        activityApi.MapPost("log", LogActivityAsync);
    }

    private static Task<IResult> GetHabitContributionsAsync(
        ClaimsPrincipal user,
        ActivityStatisticsService stats,
        string? period,
        string? tag,
        CancellationToken cancellationToken) =>
        RunStatsQueryAsync(
            user,
            (userId, ct) => stats.GetHabitContributionsAsync(userId, period, tag, ct),
            x => Results.Ok(x),
            cancellationToken);

    private static Task<IResult> GetDashboardAsync(
        ClaimsPrincipal user,
        ActivityStatisticsService stats,
        string? period,
        string? tag,
        CancellationToken cancellationToken) =>
        RunStatsQueryAsync(
            user,
            (userId, ct) => stats.GetDashboardAsync(userId, period, tag, ct),
            x => Results.Ok(x),
            cancellationToken);

    private static Task<IResult> GetDailyContributionsAsync(
        ClaimsPrincipal user,
        ActivityStatisticsService stats,
        string? period,
        string? tag,
        CancellationToken cancellationToken) =>
        RunStatsQueryAsync(
            user,
            (userId, ct) => stats.GetDailyContributionsAsync(userId, period, tag, ct),
            x => Results.Ok(x),
            cancellationToken);

    private static Task<IResult> GetActivityDayDetailAsync(
        ClaimsPrincipal user,
        ActivityStatisticsService stats,
        DateOnly date,
        string? tag,
        CancellationToken cancellationToken) =>
        RunStatsQueryAsync(
            user,
            (userId, ct) => stats.GetActivityDayDetailAsync(userId, date, tag, ct),
            x => Results.Ok(x),
            cancellationToken);

    private static async Task<IResult> RunStatsQueryAsync<T>(
        ClaimsPrincipal user,
        Func<Guid, CancellationToken, Task<T>> query,
        Func<T, IResult> toResult,
        CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        return toResult(await query(userId, cancellationToken));
    }

    private static async Task<IResult> LogActivityAsync(
        ClaimsPrincipal user,
        BoardPersistenceService persistence,
        DemoUserResolver demoUserResolver,
        ActivityLogRequest body,
        CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(user) is null)
        {
            return Results.Unauthorized();
        }

        if (body.DurationSeconds.HasValue && (body.DurationSeconds.Value < 0 || body.DurationSeconds.Value > 86400))
        {
            return Results.BadRequest(new { detail = "Duration must be between 0 and 86,400 seconds (24 hours)." });
        }

        var resolvedUserId = await demoUserResolver.ResolveUserIdAsync(user, cancellationToken);

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
    }
}
