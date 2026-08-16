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

    private static async Task<IResult> GetHabitContributionsAsync(
        CurrentUserId user,
        ActivityStatisticsService stats,
        string? period,
        string? tag,
        CancellationToken cancellationToken) =>
        Results.Ok(await stats.GetHabitContributionsAsync(user.Value, period, tag, cancellationToken));

    private static async Task<IResult> GetDashboardAsync(
        CurrentUserId user,
        ActivityStatisticsService stats,
        string? period,
        string? tag,
        CancellationToken cancellationToken) =>
        Results.Ok(await stats.GetDashboardAsync(user.Value, period, tag, cancellationToken));

    private static async Task<IResult> GetDailyContributionsAsync(
        CurrentUserId user,
        ActivityStatisticsService stats,
        string? period,
        string? tag,
        CancellationToken cancellationToken) =>
        Results.Ok(await stats.GetDailyContributionsAsync(user.Value, period, tag, cancellationToken));

    private static async Task<IResult> GetActivityDayDetailAsync(
        CurrentUserId user,
        ActivityStatisticsService stats,
        DateOnly date,
        string? tag,
        CancellationToken cancellationToken) =>
        Results.Ok(await stats.GetActivityDayDetailAsync(user.Value, date, tag, cancellationToken));

    private static async Task<IResult> LogActivityAsync(
        CurrentUserId user,
        BoardPersistenceService persistence,
        ActivityLogRequest body,
        CancellationToken cancellationToken)
    {
        if (body.DurationSeconds.HasValue && (body.DurationSeconds.Value < 0 || body.DurationSeconds.Value > 86400))
        {
            return Results.BadRequest(new { detail = "Duration must be between 0 and 86,400 seconds (24 hours)." });
        }

        if (body.EventType == ActivityEventType.TimerSession && body.DurationSeconds.HasValue)
        {
            await persistence.LogTimerSessionAsync(
                user.Value,
                TimeSpan.FromSeconds(body.DurationSeconds.Value),
                body.BoardItemId,
                body.CustomLabel,
                cancellationToken);
        }
        else
        {
            await persistence.LogActivityAsync(
                user.Value,
                body.EventType,
                body.BoardItemId,
                body.DurationSeconds,
                body.CustomLabel,
                cancellationToken);
        }
        return Results.NoContent();
    }
}
