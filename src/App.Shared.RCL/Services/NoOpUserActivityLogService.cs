namespace App.Shared.RCL.Services;

public sealed class NoOpUserActivityLogService : IUserActivityLogService
{
    public Task LogTimerSessionAsync(TimeSpan duration, Guid? boardItemId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
