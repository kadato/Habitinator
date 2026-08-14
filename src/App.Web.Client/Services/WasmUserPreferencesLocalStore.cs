using System.Security.Claims;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace App.Web.Client.Services;

/// <summary>Browser storage half of the shared local-first preferences service.</summary>
internal sealed class WasmUserPreferencesLocalStore : IUserPreferencesLocalStore
{
    private const string PreferencesKey = "user_preferences_v1";

    private readonly IJSInProcessRuntime? _js;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<WasmUserPreferencesLocalStore> _logger;
    private bool _isLoggedIn;

    public WasmUserPreferencesLocalStore(
        IJSRuntime js,
        AuthenticationStateProvider authStateProvider,
        ILogger<WasmUserPreferencesLocalStore> logger)
    {
        _js = js as IJSInProcessRuntime;
        _authStateProvider = authStateProvider;
        _logger = logger;
        _authStateProvider.AuthenticationStateChanged += _ => SessionChanged?.Invoke();
    }

    public bool IsLoggedIn => _isLoggedIn;

    public event Action? SessionChanged;

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<string> GetKeyAsync(CancellationToken cancellationToken = default)
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        _isLoggedIn = authState.User.Identity?.IsAuthenticated == true;
        var email = authState.User.FindFirst(ClaimTypes.Email)?.Value
                    ?? authState.User.Identity?.Name;
        return LocalFirstRemoteStore.KeyFor(email, PreferencesKey);
    }

    public UserPreferences ReadLocal(string key)
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

    public void WriteLocal(string key, UserPreferences preferences)
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
}
