using System.Net.Http.Json;
using System.Text.Json;

using App.Shared.RCL.Models;

namespace App.MAUI.Services;

public sealed class ApiAuthService
{
    private static readonly JsonSerializerOptions Serializer = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _http;

    public ApiAuthService(IHttpClientFactory http)
    {
        _http = http;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var client = _http.CreateClient("apiAuth");
        using var res = await client.PostAsJsonAsync("api/auth/login", request, Serializer, cancellationToken);
        if (!res.IsSuccessStatusCode) return null;

        return await res.Content.ReadFromJsonAsync<LoginResponse>(Serializer, cancellationToken);
    }

    /// <summary>Sign in as the server-configured demo guest (JWT), same user as web guest-login.</summary>
    public async Task<LoginResponse?> GuestJwtLoginAsync(CancellationToken cancellationToken = default)
    {
        var client = _http.CreateClient("apiAuth");
        using var res = await client.PostAsync("api/auth/guest-jwt", null, cancellationToken);
        if (!res.IsSuccessStatusCode) return null;

        return await res.Content.ReadFromJsonAsync<LoginResponse>(Serializer, cancellationToken);
    }

    public async Task<bool> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var client = _http.CreateClient("apiAuth");
        using var res = await client.PostAsJsonAsync("api/auth/register", request, Serializer, cancellationToken);
        return res.IsSuccessStatusCode;
    }
}
