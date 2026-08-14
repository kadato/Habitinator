using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>
///     Platform-specific half of <see cref="LocalFirstUserPreferencesService" />: per-user key,
///     session readiness, and the local persistence surface: browser storage on WASM, MAUI
///     preferences on the app. Keeps the two clients on the exact same remote behavior.
/// </summary>
public interface IUserPreferencesLocalStore
{
    Task<string> GetKeyAsync(CancellationToken cancellationToken = default);

    Task EnsureReadyAsync(CancellationToken cancellationToken = default);

    bool IsLoggedIn { get; }

    UserPreferences ReadLocal(string key);

    void WriteLocal(string key, UserPreferences preferences);

    event Action? SessionChanged;
}
