using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

namespace App.MAUI.Services;

/// <summary>
///     MAUI preferences half of the shared local-first preferences service. Falls back to device
///     preferences when offline or not authenticated.
/// </summary>
public sealed class MauiUserPreferencesLocalStore : IUserPreferencesLocalStore
{
    private const string PreferencesKey = "user_preferences_v1";

    private readonly IApiSession _apiSession;

    public MauiUserPreferencesLocalStore(IApiSession apiSession)
    {
        _apiSession = apiSession;
        _apiSession.Changed += OnSessionChanged;
    }

    public bool IsLoggedIn => _apiSession.IsLoggedIn;

    public event Action? SessionChanged;

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!_apiSession.IsReady)
        {
            await _apiSession.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<string> GetKeyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(LocalFirstRemoteStore.KeyFor(_apiSession.Email, PreferencesKey));

    public UserPreferences ReadLocal(string key)
    {
        var json = Preferences.Get(key, null);
        return UserPreferencesJson.DeserializeOrDefault(json);
    }

    public void WriteLocal(string key, UserPreferences preferences)
    {
        Preferences.Set(key, UserPreferencesJson.Serialize(preferences));
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        SessionChanged?.Invoke();
    }
}
