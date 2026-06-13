using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Client.Services;

public sealed class WasmAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpClientFactory _http;
    private AuthenticationState? _cache;

    public WasmAuthenticationStateProvider(IHttpClientFactory http)
    {
        _http = http;
    }

    private HttpClient Client => _http.CreateClient("api");

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_cache is not null) return _cache;

        try
        {
            var status = await Client.GetFromJsonAsync<AuthStatusDto>("api/auth/status");
            if (status is { IsAuthenticated: true, Email: not null })
            {
                var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, status.Email),
                    new Claim(ClaimTypes.Email, status.Email)
                ], "cookie");

                var principal = new ClaimsPrincipal(identity);
                _cache = new AuthenticationState(principal);
                return _cache;
            }
        }
        catch
        {
            // Ignore and fall back to anonymous
        }

        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        _cache = new AuthenticationState(anonymous);
        return _cache;
    }

    private sealed record AuthStatusDto(bool IsAuthenticated, string? Email);
}
