using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services.Remote;

public sealed class RemoteUserActivityLogService : IUserActivityLogService
{
    private readonly IHttpClientFactory _http;

    public RemoteUserActivityLogService(IHttpClientFactory http)
    {
        _http = http;
    }

    private HttpClient Client => _http.CreateClient("api");

    public async Task LogActivityAsync(
        ActivityEventType eventType,
        Guid? boardItemId,
        int? durationSeconds = null,
        string? itemTitleSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var req = new ActivityLogRequest(eventType, boardItemId, durationSeconds, itemTitleSnapshot);
            using var res = await Client.PostAsJsonAsync("api/activity/log", req, cancellationToken);
            res.EnsureSuccessStatusCode();
        }
        catch
        {
            // best-effort
        }
    }

    public async Task LogTimerSessionAsync(
        TimeSpan duration,
        Guid? boardItemId,
        string? customLabel = null,
        CancellationToken cancellationToken = default)
    {
        var sec = (int)Math.Min(int.MaxValue, Math.Max(0, duration.TotalSeconds));
        if (sec == 0)
        {
            return;
        }

        try
        {
            var req = new ActivityLogRequest(ActivityEventType.TimerSession, boardItemId, sec, customLabel);
            using var res = await Client.PostAsJsonAsync("api/activity/log", req, cancellationToken);
            res.EnsureSuccessStatusCode();
        }
        catch
        {
            // best-effort
        }
    }
}

public sealed record ActivityLogRequest(
    ActivityEventType EventType,
    Guid? BoardItemId,
    int? DurationSeconds,
    string? CustomLabel);
