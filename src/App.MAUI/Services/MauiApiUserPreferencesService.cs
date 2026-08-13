using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.Extensions.Logging;

namespace App.MAUI.Services;

/// <summary>
///     When logged in, loads and saves user preferences on the server.
///     Falls back to <see cref="Preferences" /> when offline or not authenticated.
/// </summary>
#pragma warning disable CA1001 // DI singleton: owns a long-lived LocalFirstRemoteStore and is never disposed by the container.
public sealed class MauiApiUserPreferencesService : IUserPreferencesService
{
    private const string PreferencesKey = "user_preferences_v1";

    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;

    private readonly IApiSession _apiSession;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<MauiApiUserPreferencesService> _logger;
    private readonly LocalFirstRemoteStore<UserPreferences> _store;

    public MauiApiUserPreferencesService(
        IHttpClientFactory http,
        IApiSession apiSession,
        ILogger<MauiApiUserPreferencesService> logger)
    {
        _apiSession = apiSession;
        _http = http;
        _logger = logger;
        _store = new LocalFirstRemoteStore<UserPreferences>(
            ReadLocal,
            WriteLocal,
            UserPreferencesJson.Serialize,
            logger);
        _apiSession.Changed += OnSessionChanged;
    }

    private HttpClient Client => _http.CreateClient("api");

    public event EventHandler? Changed;

    private string GetKey() => LocalFirstRemoteStore.KeyFor(_apiSession.Email, PreferencesKey);

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSessionReadyAsync(cancellationToken).ConfigureAwait(false);
        var key = GetKey();
        var localPrefs = _store.GetLocal(key);

        if (_apiSession.IsLoggedIn)
        {
            _store.RefreshInBackground(
                key,
                localPrefs,
                FetchRemoteAsync,
                () => Changed?.Invoke(this, EventArgs.Empty),
                cancellationToken);
        }

        return localPrefs;
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        await EnsureSessionReadyAsync(cancellationToken).ConfigureAwait(false);
        var key = GetKey();
        await _store.WriteLocalAsync(key, preferences, cancellationToken).ConfigureAwait(false);

        if (!_apiSession.IsLoggedIn)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            using var res = await Client
                .PutAsJsonAsync("api/settings/preferences", preferences, Serializer, cancellationToken)
                .ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            // Best-effort save. The local copy is already updated
            _logger.LogDebug(ex, "Failed to save user preferences to the server.");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<UserPreferences?> FetchRemoteAsync(CancellationToken cancellationToken)
    {
        if (!_apiSession.IsLoggedIn)
        {
            return null;
        }

        using var res = await Client.GetAsync("api/settings/preferences", cancellationToken).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            return null;
        }

        return await res.Content.ReadFromJsonAsync<UserPreferences>(Serializer, cancellationToken).ConfigureAwait(false);
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

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
#pragma warning restore CA1001
