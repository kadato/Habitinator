using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Auth;

/// <summary>
/// Exposes the current HTTP user (Identity cookie) to Blazor so <c>AuthenticationStateProvider</c> matches the request.
/// </summary>
/// <remarks>
/// In interactive Blazor Web Apps, <see cref="IHttpContextAccessor.HttpContext"/> is often null on the SignalR
/// callback context after the WebSocket is established. The first call that runs during the initial request (or
/// prerender) can read the real principal; we cache it for that circuit so <see cref="AuthenticationState"/>
/// and services (e.g. <c>IBoardDataService</c>) stay consistent and do not use an anonymous user by mistake.
/// </remarks>
public sealed class HttpContextAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private ClaimsPrincipal? _circuitUser;

    public HttpContextAuthenticationStateProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        HttpContext? http = _httpContextAccessor.HttpContext;
        if (http is not null)
        {
            _circuitUser = http.User;
        }

        ClaimsPrincipal user = _circuitUser
            ?? new ClaimsPrincipal(new ClaimsIdentity());
        return Task.FromResult(new AuthenticationState(user));
    }
}
