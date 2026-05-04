using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

namespace App.MAUI.Services;

/// <summary>
///     When logged in, loads and saves user preferences on the server.
///     Falls back to <see cref="Preferences" /> when offline or not authenticated.
/// </summary>
public sealed class MauiApiUserPreferencesService : IUserPreferencesService
{
    private const string PreferencesKey = "user_preferences_v1";

    private static readonly JsonSerializerOptions Serializer = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IApiSession _apiSession;

    private readonly IHttpClientFactory _http;

    public MauiApiUserPreferencesService(IHttpClientFactory http, IApiSession apiSession)
    {
        _http = http;
        _apiSession = apiSession;
        _apiSession.Changed += (_, _) => Changed?.Invoke();
    }

    private HttpClient Client => _http.CreateClient("api");

    public event Action? Changed;

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSessionReadyAsync(cancellationToken).ConfigureAwait(false);
        if (!_apiSession.IsLoggedIn) return ReadLocal();

        try
        {
            using var res = await Client.GetAsync("api/settings/preferences", cancellationToken)
                .ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return ReadLocal();

            var remote = await res.Content
                .ReadFromJsonAsync<UserPreferences>(Serializer, cancellationToken)
                .ConfigureAwait(false);
            if (remote is null) return ReadLocal();

            WriteLocal(remote);
            return remote;
        }
        catch (HttpRequestException)
        {
            return ReadLocal();
        }
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        await EnsureSessionReadyAsync(cancellationToken).ConfigureAwait(false);
        WriteLocal(preferences);

        if (!_apiSession.IsLoggedIn)
        {
            Changed?.Invoke();
            return;
        }

        using var res = await Client
            .PutAsJsonAsync("api/settings/preferences", preferences, Serializer, cancellationToken)
            .ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        Changed?.Invoke();
    }

    private async Task EnsureSessionReadyAsync(CancellationToken cancellationToken)
    {
        if (!_apiSession.IsReady) await _apiSession.LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    private UserPreferences ReadLocal()
    {
        var json = Preferences.Get(PreferencesKey, null);
        return UserPreferencesJson.DeserializeOrDefault(json);
    }

    private static void WriteLocal(UserPreferences preferences)
    {
        Preferences.Set(PreferencesKey, UserPreferencesJson.Serialize(preferences));
    }
}
