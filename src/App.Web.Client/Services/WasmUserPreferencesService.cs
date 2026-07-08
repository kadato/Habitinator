using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace App.Web.Client.Services;

public sealed class WasmUserPreferencesService : IUserPreferencesService
{
    private const string PreferencesKey = "user_preferences_v1";

    private static readonly JsonSerializerOptions Serializer = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _http;
    private readonly IJSInProcessRuntime? _js;
    private readonly AuthenticationStateProvider _authStateProvider;

    public WasmUserPreferencesService(IHttpClientFactory http, IJSRuntime js, AuthenticationStateProvider authStateProvider)
    {
        _http = http;
        _js = js as IJSInProcessRuntime;
        _authStateProvider = authStateProvider;
    }

    private HttpClient Client => _http.CreateClient("api");

    public event Action? Changed;

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
        var localPrefs = ReadLocal(key);

        // Fetch remote preferences in the background
        _ = Task.Run(async () =>
        {
            try
            {
                var authState = await _authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
                if (authState.User.Identity?.IsAuthenticated != true)
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
                            Changed?.Invoke();
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
        var key = await GetKeyAsync().ConfigureAwait(false);
        WriteLocal(key, preferences);

        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
            if (authState.User.Identity?.IsAuthenticated == true)
            {
                using var res = await Client.PutAsJsonAsync("api/settings/preferences", preferences, Serializer, cancellationToken).ConfigureAwait(false);
                res.EnsureSuccessStatusCode();
            }
        }
        catch
        {
            // Best-effort write to remote; local is updated
        }
        Changed?.Invoke();
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
        catch
        {
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
        catch
        {
            // Ignore storage errors in browser
        }
    }
}
