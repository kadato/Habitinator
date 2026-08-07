using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace App.Web.Client.Services;

internal sealed class WasmUserPreferencesService : IUserPreferencesService, IDisposable
{
    private const string PreferencesKey = "user_preferences_v1";

    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;

    private readonly IHttpClientFactory _http;
    private readonly IJSInProcessRuntime? _js;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<WasmUserPreferencesService> _logger;
    private readonly LocalFirstRemoteStore<UserPreferences> _store;

    public WasmUserPreferencesService(
        IHttpClientFactory http,
        IJSRuntime js,
        AuthenticationStateProvider authStateProvider,
        ILogger<WasmUserPreferencesService> logger)
    {
        _http = http;
        _js = js as IJSInProcessRuntime;
        _authStateProvider = authStateProvider;
        _logger = logger;
        _store = new LocalFirstRemoteStore<UserPreferences>(
            ReadLocal,
            WriteLocal,
            UserPreferencesJson.Serialize,
            logger);
    }

    private HttpClient Client => _http.CreateClient("api");

    public event EventHandler? Changed;

    private async Task<string> GetKeyAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        var email = authState.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                    ?? authState.User.Identity?.Name;
        return string.IsNullOrEmpty(email) ? PreferencesKey : $"{PreferencesKey}_{email}";
    }

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        var key = await GetKeyAsync().ConfigureAwait(false);
        var localPrefs = _store.GetLocal(key);

        _store.RefreshInBackground(
            key,
            localPrefs,
            FetchRemoteAsync,
            () => Changed?.Invoke(this, EventArgs.Empty),
            cancellationToken);

        return localPrefs;
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        var key = await GetKeyAsync().ConfigureAwait(false);
        await _store.WriteLocalAsync(key, preferences, cancellationToken).ConfigureAwait(false);

        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
            if (authState.User.Identity?.IsAuthenticated == true)
            {
                using var res = await Client.PutAsJsonAsync("api/settings/preferences", preferences, Serializer, cancellationToken).ConfigureAwait(false);
                res.EnsureSuccessStatusCode();
            }
        }
        catch (Exception ex)
        {
            // Best-effort write to remote; local is updated
            _logger.LogDebug(ex, "Failed to save user preferences to the server.");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<UserPreferences?> FetchRemoteAsync(CancellationToken cancellationToken)
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        if (authState.User.Identity?.IsAuthenticated != true)
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

    private UserPreferences ReadLocal(string key)
    {
        if (_js is null)
        {
            return new UserPreferences();
        }

        try
        {
            var json = _js.Invoke<string?>("localStorage.getItem", key);
            return UserPreferencesJson.DeserializeOrDefault(json);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read user preferences from localStorage.");
            return new UserPreferences();
        }
    }

    private void WriteLocal(string key, UserPreferences preferences)
    {
        if (_js is null)
        {
            return;
        }

        try
        {
            _js.InvokeVoid("localStorage.setItem", key, UserPreferencesJson.Serialize(preferences));
        }
        catch (Exception ex)
        {
            // Ignore storage errors in browser
            _logger.LogDebug(ex, "Failed to write user preferences to localStorage.");
        }
    }

    public void Dispose()
    {
        _store.Dispose();
    }
}
