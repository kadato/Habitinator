using System.Text.Json;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services.Remote;

public sealed class RemoteUserActivityLogService : IUserActivityLogService
{
    private const string PendingKey = "habitinator_activity_pending_v1";
    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;

    private readonly IHttpClientFactory _http;
    private readonly IActivityStatisticsReader? _statsReader;
    private readonly IActivityEventStore? _eventStore;
    private readonly ILocalSettingsStore? _localStore;
    private readonly IClock? _clock;

    public RemoteUserActivityLogService(
        IHttpClientFactory http,
        IActivityStatisticsReader? statsReader = null,
        IActivityEventStore? eventStore = null,
        ILocalSettingsStore? localStore = null,
        IClock? clock = null)
    {
        _http = http;
        _statsReader = statsReader;
        _eventStore = eventStore;
        _localStore = localStore;
        _clock = clock;
    }

    private HttpClient Client => _http.CreateClient("api");

    public async Task LogActivityAsync(
        ActivityEventType eventType,
        Guid? boardItemId,
        int? durationSeconds = null,
        string? itemTitleSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        var record = new UserActivityEventRecord(
            _clock?.UtcNow ?? DateTimeOffset.UtcNow,
            eventType,
            boardItemId,
            durationSeconds,
            itemTitleSnapshot);

        if (_eventStore != null)
        {
            try
            {
                await _eventStore.AppendAsync(record, cancellationToken);
            }
            catch (Exception ex)
            {
                // Ignore - best effort local store; remote will be attempted anyway
                _ = ex;
            }
        }

        await PostBestEffortAsync(
            new ActivityLogRequest(eventType, boardItemId, durationSeconds, itemTitleSnapshot),
            cancellationToken);
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

        var record = new UserActivityEventRecord(
            _clock?.UtcNow ?? DateTimeOffset.UtcNow,
            ActivityEventType.TimerSession,
            boardItemId,
            sec,
            customLabel);

        if (_eventStore != null)
        {
            try
            {
                await _eventStore.AppendAsync(record, cancellationToken);
            }
            catch (Exception ex)
            {
                // Ignore - best effort local store; remote will be attempted anyway
                _ = ex;
            }
        }

        await PostBestEffortAsync(
            new ActivityLogRequest(ActivityEventType.TimerSession, boardItemId, sec, customLabel),
            cancellationToken);
    }

    private async Task PostBestEffortAsync(ActivityLogRequest req, CancellationToken cancellationToken)
    {
        // First try to flush any pending queue when online
        await TryFlushPendingAsync(cancellationToken);

        try
        {
            using var res = await Client.PostAsJsonAsync("api/activity/log", req, cancellationToken);
            res.EnsureSuccessStatusCode();
            _statsReader?.InvalidateCache();
        }
        catch
        {
            // Queue for later sync via outbox pattern
            EnqueuePending(req);
        }
    }

    private void EnqueuePending(ActivityLogRequest req)
    {
        if (_localStore == null)
        {
            return;
        }

        try
        {
            var raw = _localStore.Read(PendingKey);
            var list = string.IsNullOrEmpty(raw)
                ? []
                : JsonSerializer.Deserialize<List<ActivityLogRequest>>(raw, Serializer) ?? [];
            list.Add(req);
            if (list.Count > 1000)
            {
                list.RemoveRange(0, list.Count - 1000);
            }

            _localStore.Write(PendingKey, JsonSerializer.Serialize(list, Serializer));
        }
        catch (Exception ex)
        {
            // Ignore - best effort to enqueue pending request for later sync
            _ = ex;
        }
    }

    public async Task TryFlushPendingAsync(CancellationToken cancellationToken = default)
    {
        if (_localStore == null)
        {
            return;
        }

        List<ActivityLogRequest> pending;
        try
        {
            var raw = _localStore.Read(PendingKey);
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            pending = JsonSerializer.Deserialize<List<ActivityLogRequest>>(raw, Serializer) ?? [];
            if (pending.Count == 0)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            // Ignore - corrupted pending queue, treat as empty
            _ = ex;
            return;
        }

        var remaining = new List<ActivityLogRequest>();
        foreach (var p in pending)
        {
            try
            {
                using var res = await Client.PostAsJsonAsync("api/activity/log", p, cancellationToken);
                res.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                // Ignore - keep remaining pending for next flush attempt
                _ = ex;
                remaining.Add(p);
                // Keep remaining pending in order
                var idx = pending.IndexOf(p);
                for (var i = idx + 1; i < pending.Count; i++)
                {
                    remaining.Add(pending[i]);
                }

                break;
            }
        }

        try
        {
            if (remaining.Count == 0)
            {
                _localStore.Write(PendingKey, "");
            }
            else if (remaining.Count != pending.Count)
            {
                _localStore.Write(PendingKey, JsonSerializer.Serialize(remaining, Serializer));
            }
        }
        catch (Exception ex)
        {
            // Ignore - best effort to persist remaining pending queue
            _ = ex;
        }

        if (remaining.Count != pending.Count)
        {
            _statsReader?.InvalidateCache();
        }
    }
}

public sealed record ActivityLogRequest(
    ActivityEventType EventType,
    Guid? BoardItemId,
    int? DurationSeconds,
    string? CustomLabel);
