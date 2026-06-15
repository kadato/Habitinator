using System.Security.Claims;
using App.Web.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace App.Web.Auth;

/// <summary>
///     Server-side authentication state for interactive Blazor, with periodic security-stamp revalidation.
///     Replaces reading <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor.HttpContext" /> on each call,
///     which is unavailable during interactive rendering and breaks auth-dependent services.
/// </summary>
internal sealed class IdentityRevalidatingAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<IdentityOptions> _options;
    private readonly PersistentComponentState _state;
    private readonly PersistingComponentStateSubscription _subscription;

    public IdentityRevalidatingAuthenticationStateProvider(
        ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<IdentityOptions> options,
        PersistentComponentState state)
        : base(loggerFactory)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _state = state;
        _subscription = state.RegisterOnPersisting(PersistAuthenticationStateAsync);
    }

    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await ValidateSecurityStampAsync(userManager, authenticationState.User).ConfigureAwait(false);
    }

    private async Task<bool> ValidateSecurityStampAsync(
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        if (!userManager.SupportsUserSecurityStamp)
        {
            return true;
        }

        var principalStamp = principal.FindFirstValue(_options.Value.ClaimsIdentity.SecurityStampClaimType);
        var userStamp = await userManager.GetSecurityStampAsync(user).ConfigureAwait(false);
        return principalStamp == userStamp;
    }

    private async Task PersistAuthenticationStateAsync()
    {
        var authState = await GetAuthenticationStateAsync().ConfigureAwait(false);
        var user = authState.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.Identity.Name;
            if (email is not null)
            {
                _state.PersistAsJson("auth_state", new AuthStatusDto(true, email));
            }
        }
        else
        {
            _state.PersistAsJson("auth_state", new AuthStatusDto(false, null));
        }
    }

    protected override void Dispose(bool disposing)
    {
        _subscription.Dispose();
        base.Dispose(disposing);
    }

    private sealed record AuthStatusDto(bool IsAuthenticated, string? Email);
}
