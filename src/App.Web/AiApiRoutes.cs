using App.Shared.RCL.Models;
using App.Web.Auth;
using App.Web.Data;
using App.Web.Services;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace App.Web;

internal static class AiApiRoutes
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    internal static void MapAiApi(this IEndpointRouteBuilder endpoints)
    {
        var aiApi = endpoints.MapGroup("/api/ai")
            .DisableAntiforgery()
            .RequireAuthorization("BoardOrJwt")
            .RequireRateLimiting("api")
            .WithDtoValidation();

        aiApi.MapPost("/test", TestConnectionAsync);
        aiApi.MapPost("/chat", ChatAsync);
        aiApi.MapPost("/chat/stream", StreamChatAsync);
    }

    private static async Task<Ok<AiTestConnectionResponse>> TestConnectionAsync(
        AiTestConnectionRequest request,
        AiAssistantService aiService,
        CancellationToken cancellationToken)
    {
        var result = await aiService.TestConnectionAsync(request, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<AiChatResponse>, NotFound>> ChatAsync(
        CurrentUserId user,
        AiChatRequest request,
        ApplicationDbContext db,
        AiAssistantService aiService,
        CancellationToken cancellationToken)
    {
        var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == user.Value, cancellationToken);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        var preferences = row.UserPreferences?.Ai ?? new AiPreferences();
        var result = await aiService.ChatAsync(user.Value, preferences, request, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task StreamChatAsync(
        CurrentUserId user,
        AiChatRequest request,
        ApplicationDbContext db,
        AiAssistantService aiService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == user.Value, cancellationToken);
        if (row is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        var preferences = row.UserPreferences?.Ai ?? new AiPreferences();
        await foreach (var evt in aiService.StreamChatAsync(user.Value, preferences, request, cancellationToken))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(evt, Json);
            await httpContext.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }

        await httpContext.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }
}
