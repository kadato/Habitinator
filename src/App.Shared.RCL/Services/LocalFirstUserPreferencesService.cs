using System.Text.Json;

using App.Shared.RCL.Models;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

/// <summary>
///     Local-first user preferences shared by the WASM web client and MAUI: reads return instantly
///     from the platform store, saves persist locally and best-effort to the server, and a background
///     refresh keeps the local copy aligned with the server.
/// </summary>
public sealed class LocalFirstUserPreferencesService : IUserPreferencesService, IDisposable
{
    private const string PreferencesKey = "user_preferences_v1";

    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;

    private readonly IHttpClientFactory _http;
    private readonly IClientSessionProvider _sessionProvider;
    private readonly ILogger<LocalFirstUserPreferencesService> _logger;
    private readonly LocalFirstRemoteStore<UserPreferences> _remoteStore;

    public event EventHandler? Changed;

    public LocalFirstUserPreferencesService(
        IHttpClientFactory http,
        IClientSessionProvider sessionProvider,
        ILocalSettingsStore localStore,
        ILogger<LocalFirstUserPreferencesService> logger)
    {
        _http = http;
        _sessionProvider = sessionProvider;
        _logger = logger;
        _remoteStore = new LocalFirstRemoteStore<UserPreferences>(
            key => UserPreferencesJson.DeserializeOrDefault(localStore.Read(key)),
            (key, prefs) => localStore.Write(key, UserPreferencesJson.Serialize(prefs)),
            UserPreferencesJson.Serialize,
            logger);
        _sessionProvider.Changed += OnSessionChanged;
    }

    private HttpClient Client => _http.CreateClient("api");

    private string GetKey() => LocalFirstRemoteStore.KeyFor(_sessionProvider.Email, PreferencesKey);

    public Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        var key = GetKey();
        var localPrefs = _remoteStore.GetLocal(key);

        if (_sessionProvider.IsLoggedIn)
        {
            _remoteStore.RefreshInBackground(
                key,
                localPrefs,
                FetchRemoteAsync,
                () => Changed?.Invoke(this, EventArgs.Empty),
                cancellationToken);
        }

        return Task.FromResult(localPrefs);
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        var key = GetKey();
        await _remoteStore.WriteLocalAsync(key, preferences, cancellationToken).ConfigureAwait(false);

        if (_sessionProvider.IsLoggedIn)
        {
            await LocalFirstSaves.PutBestEffortAsync(
                Client,
                "api/settings/preferences",
                preferences,
                Serializer,
                _logger,
                cancellationToken).ConfigureAwait(false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<UserPreferences?> FetchRemoteAsync(CancellationToken cancellationToken)
    {
        if (!_sessionProvider.IsLoggedIn)
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

    public void Dispose()
    {
        _sessionProvider.Changed -= OnSessionChanged;
        _remoteStore.Dispose();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
