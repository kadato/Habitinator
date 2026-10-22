using App.Shared.RCL.Models;
using App.Web.Auth;
using App.Web.Data;
using App.Web.Services;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace App.Web;

internal static class AiApiRoutes
{
    internal static void MapAiApi(this IEndpointRouteBuilder endpoints)
    {
        var aiApi = endpoints.MapGroup("/api/ai")
            .DisableAntiforgery()
            .RequireAuthorization("BoardOrJwt")
            .RequireRateLimiting("api")
            .WithDtoValidation();

        aiApi.MapPost("/test", TestConnectionAsync);
        aiApi.MapPost("/chat", ChatAsync);
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
}
