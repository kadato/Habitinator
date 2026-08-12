using System.Security.Claims;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class CurrentUserAccessor(DemoUserResolver demoUserResolver)
{
    public async Task<Guid?> TryResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return await demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
    }

    public async Task<Guid?> TryResolveAsync(AuthenticationStateProvider authStateProvider, CancellationToken cancellationToken = default)
    {
        var state = await authStateProvider.GetAuthenticationStateAsync();
        return await TryResolveAsync(state.User, cancellationToken);
    }

    public async Task<Guid> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("Sign in required.");
        }

        return await demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
    }

    public async Task<Guid> ResolveAsync(AuthenticationStateProvider authStateProvider, CancellationToken cancellationToken = default)
    {
        var state = await authStateProvider.GetAuthenticationStateAsync();
        return await ResolveAsync(state.User, cancellationToken);
    }
}
