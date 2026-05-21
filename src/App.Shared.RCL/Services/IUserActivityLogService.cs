using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public interface IUserActivityLogService
{
    Task LogActivityAsync(
        ActivityEventType eventType,
        Guid? boardItemId,
        int? durationSeconds = null,
        string? itemTitleSnapshot = null,
        CancellationToken cancellationToken = default);

    /// <summary>Records a focus timer session for statistics (independent of board progress updates).</summary>
    Task LogTimerSessionAsync(TimeSpan duration, Guid? boardItemId, string? customLabel = null, CancellationToken cancellationToken = default);
}
