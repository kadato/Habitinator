using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Auth;
using App.Web.Data;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

internal sealed class WebAiAssistantService(
    AiAssistantService service,
    AuthenticationStateProvider authenticationStateProvider,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    AiApiKeyProtector apiKeyProtector) : IAiAssistantService
{
    public async Task<AiTestConnectionResponse> TestConnectionAsync(
        AiTestConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = AuthenticatedUserId.TryGet(state.User);
        if (userId is null)
        {
            return await service.TestConnectionAsync(request, cancellationToken: cancellationToken);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.ApiKey) && row is not null)
        {
            request.ApiKey = apiKeyProtector.Unprotect(row.UserPreferences?.Ai?.ApiKey);
        }

        return await service.TestConnectionAsync(request, userId.Value, cancellationToken);
    }

    public async Task<AiChatResponse> SendChatAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = AuthenticatedUserId.TryGet(state.User);
        if (userId is null)
        {
            return new AiChatResponse
            {
                Success = false,
                Error = "You must be signed in to use the AI assistant."
            };
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);
        var preferences = AiPreferencesPrivacy.UnprotectForProvider(row?.UserPreferences?.Ai, apiKeyProtector);
        return await service.ChatAsync(userId.Value, preferences, request, cancellationToken);
    }

    public async IAsyncEnumerable<AiChatStreamEvent> StreamChatAsync(
        AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = AuthenticatedUserId.TryGet(state.User);
        if (userId is null)
        {
            yield return new AiChatStreamEvent
            {
                Type = "error",
                Error = "You must be signed in to use the AI assistant."
            };
            yield break;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);
        var preferences = AiPreferencesPrivacy.UnprotectForProvider(row?.UserPreferences?.Ai, apiKeyProtector);

        await foreach (var evt in service.StreamChatAsync(userId.Value, preferences, request, cancellationToken))
        {
            yield return evt;
        }
    }
}
