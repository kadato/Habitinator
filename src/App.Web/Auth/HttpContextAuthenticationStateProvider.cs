using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Auth;

/// <summary>
/// Exposes the current HTTP user (Identity cookie) to Blazor so <c>AuthenticationStateProvider</c> matches the request.
/// </summary>
public sealed class HttpContextAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextAuthenticationStateProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        ClaimsPrincipal user = _httpContextAccessor.HttpContext?.User
            ?? new ClaimsPrincipal(new ClaimsIdentity());
        return Task.FromResult(new AuthenticationState(user));
    }
}
