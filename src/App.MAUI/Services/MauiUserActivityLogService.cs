using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

namespace App.MAUI.Services;

public sealed class MauiUserActivityLogService : IUserActivityLogService
{
    private readonly MauiActivityEventStore _store;

    public MauiUserActivityLogService(MauiActivityEventStore store)
    {
        _store = store;
    }

    public async Task LogActivityAsync(
        ActivityEventType eventType,
        Guid? boardItemId,
        int? durationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        await _store.AppendAsync(new StoredUserActivityEvent
        {
            Id = Guid.NewGuid(),
            UserId = MauiLocalUser.Id,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            EventType = eventType,
            BoardItemId = boardItemId,
            DurationSeconds = eventType == ActivityEventType.TimerSession ? durationSeconds : null
        }, cancellationToken);
    }

    public Task LogTimerSessionAsync(TimeSpan duration, Guid? boardItemId,
        CancellationToken cancellationToken = default)
    {
        var sec = (int)Math.Min(int.MaxValue, Math.Max(0, duration.TotalSeconds));
        if (sec == 0) return Task.CompletedTask;

        return LogActivityAsync(ActivityEventType.TimerSession, boardItemId, sec, cancellationToken);
    }
}
