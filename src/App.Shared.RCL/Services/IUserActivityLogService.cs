namespace App.Shared.RCL.Services;

public interface IUserActivityLogService
{
    /// <summary>Records a focus timer session for statistics (independent of board progress updates).</summary>
    Task LogTimerSessionAsync(TimeSpan duration, Guid? boardItemId, CancellationToken cancellationToken = default);
}
