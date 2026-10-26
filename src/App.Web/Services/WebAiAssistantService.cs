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
    IDbContextFactory<ApplicationDbContext> dbFactory) : IAiAssistantService
{
    public Task<AiTestConnectionResponse> TestConnectionAsync(
        AiTestConnectionRequest request,
        CancellationToken cancellationToken = default)
        => service.TestConnectionAsync(request, cancellationToken);

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
        var preferences = row?.UserPreferences?.Ai ?? new AiPreferences();
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
        var preferences = row?.UserPreferences?.Ai ?? new AiPreferences();

        await foreach (var evt in service.StreamChatAsync(userId.Value, preferences, request, cancellationToken))
        {
            yield return evt;
        }
    }
}
