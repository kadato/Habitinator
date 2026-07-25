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
        _apiSession.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    private HttpClient Client => _http.CreateClient("api");

    public event EventHandler? Changed;

    private string GetKey()
    {
        var email = _apiSession.Email;
        return string.IsNullOrEmpty(email) ? PreferencesKey : $"{PreferencesKey}_{email}";
    }

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSessionReadyAsync(cancellationToken).ConfigureAwait(false);
        var key = GetKey();
        var localPrefs = ReadLocal(key);

        // Fetch remote preferences in the background
        _ = Task.Run(async () =>
        {
            try
            {
                if (!_apiSession.IsLoggedIn)
                {
                    return;
                }

                using var res = await Client.GetAsync("api/settings/preferences", CancellationToken.None).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    var remote = await res.Content.ReadFromJsonAsync<UserPreferences>(Serializer, CancellationToken.None).ConfigureAwait(false);
                    if (remote is not null)
                    {
                        var remoteJson = UserPreferencesJson.Serialize(remote);
                        var localJson = UserPreferencesJson.Serialize(localPrefs);
                        if (remoteJson != localJson)
                        {
                            WriteLocal(key, remote);
                            Changed?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
            }
            catch
            {
                // Best-effort remote sync, ignore errors in background task
            }
        }, CancellationToken.None);

        return localPrefs;
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        await EnsureSessionReadyAsync(cancellationToken).ConfigureAwait(false);
        var key = GetKey();
        WriteLocal(key, preferences);

        if (!_apiSession.IsLoggedIn)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        using var res = await Client
            .PutAsJsonAsync("api/settings/preferences", preferences, Serializer, cancellationToken)
            .ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task EnsureSessionReadyAsync(CancellationToken cancellationToken)
    {
        if (!_apiSession.IsReady)
        {
            await _apiSession.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static UserPreferences ReadLocal(string key)
    {
        var json = Preferences.Get(key, null);
        return UserPreferencesJson.DeserializeOrDefault(json);
    }

    private static void WriteLocal(string key, UserPreferences preferences)
    {
        Preferences.Set(key, UserPreferencesJson.Serialize(preferences));
    }
}
