using System.Net;
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
        if (!res.IsSuccessStatusCode)
        {
            return null;
        }

        return await res.Content.ReadFromJsonAsync<LoginResponse>(Serializer, cancellationToken);
    }

    /// <summary>Sign in as the server-configured demo guest (JWT), same user as web guest-login.</summary>
    public async Task<LoginResponse?> GuestJwtLoginAsync(CancellationToken cancellationToken = default)
    {
        var client = _http.CreateClient("apiAuth");
        using var res = await client.PostAsync("api/auth/guest-jwt", null, cancellationToken);
        if (!res.IsSuccessStatusCode)
        {
            return null;
        }

        return await res.Content.ReadFromJsonAsync<LoginResponse>(Serializer, cancellationToken);
    }

    public async Task<RegistrationResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = _http.CreateClient("apiAuth");
        HttpResponseMessage res;
        try
        {
            res = await client.PostAsJsonAsync("api/auth/register", request, Serializer, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return new RegistrationResult(false, OtherError: "Could not reach the server. Check the network and API base URL in settings.");
        }
        catch (TaskCanceledException)
        {
            return new RegistrationResult(false, OtherError: "The request was cancelled or timed out.");
        }

        if (res.IsSuccessStatusCode)
        {
            return new RegistrationResult(true);
        }

        if (res.StatusCode == HttpStatusCode.BadRequest)
        {
            var body = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(body, Serializer);
                    if (list is { Count: > 0 })
                    {
                        return new RegistrationResult(false, ErrorDetails: list);
                    }
                }
                catch (JsonException)
                {
                    // Fall back to general error if JSON parsing fails
                }
            }

            return new RegistrationResult(false, OtherError: "Registration was rejected. Check the email and password, then try again.");
        }

        if ((int)res.StatusCode is >= 500 and <= 599)
        {
            return new RegistrationResult(
                false,
                OtherError: "The server returned an error. Try again later, or check that the API is up to date and running.");
        }

        return new RegistrationResult(
            false,
            OtherError: $"Registration failed (HTTP {(int)res.StatusCode}). Check the API and try again.");
    }
}
