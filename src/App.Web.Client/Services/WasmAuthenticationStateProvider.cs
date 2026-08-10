using System.Security.Claims;

using App.Shared.RCL.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Client.Services;

internal sealed class WasmAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpClientFactory _http;
    private readonly PersistentComponentState _state;
    private AuthenticationState? _cache;

    public WasmAuthenticationStateProvider(IHttpClientFactory http, PersistentComponentState state)
    {
        _http = http;
        _state = state;
    }

    private HttpClient Client => _http.CreateClient("api");

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        // 1. Try to get the persisted state from the server prerendering
        if (_state.TryTakeFromJson<AuthStatusDto>("auth_state", out var status) && status is not null)
        {
            _cache = CreateAuthenticationState(status);
            return _cache;
        }

        // 2. Fallback to API check if state was not persisted (e.g. initial load without prerendering, or dynamic re-auth)
        try
        {
            status = await Client.GetFromJsonAsync<AuthStatusDto>("api/auth/status").ConfigureAwait(false);
            if (status is not null)
            {
                _cache = CreateAuthenticationState(status);
                return _cache;
            }
        }
        catch
        {
            // Ignore and fall back to anonymous
        }

        _cache = CreateAuthenticationState(new AuthStatusDto(false, null));
        return _cache;
    }

    private static AuthenticationState CreateAuthenticationState(AuthStatusDto status)
    {
        if (status.IsAuthenticated && status.Email is not null)
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, status.Email),
                new Claim(ClaimTypes.Email, status.Email)
            ], "cookie");

            return new AuthenticationState(new ClaimsPrincipal(identity));
        }

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }
}
