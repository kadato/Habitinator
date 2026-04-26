using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public interface INotificationSettingsService
{
    event Action? Changed;

    Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default);
}
