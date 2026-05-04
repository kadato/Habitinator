using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public interface IUserPreferencesService
{
    event Action? Changed;

    Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default);
}
