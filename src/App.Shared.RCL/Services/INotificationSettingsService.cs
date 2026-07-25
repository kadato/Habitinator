using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public interface INotificationSettingsService
{
    event EventHandler? Changed;

    Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default);
}
