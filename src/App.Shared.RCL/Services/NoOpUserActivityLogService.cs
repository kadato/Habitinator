using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public sealed class NoOpUserActivityLogService : IUserActivityLogService
{
    public Task LogActivityAsync(
        ActivityEventType eventType,
        Guid? boardItemId,
        int? durationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task LogTimerSessionAsync(TimeSpan duration, Guid? boardItemId,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
