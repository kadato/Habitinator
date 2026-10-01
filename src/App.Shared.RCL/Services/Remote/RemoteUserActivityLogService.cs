using System.Text.Json;

using App.Shared.RCL.Models;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services.Remote;

public sealed class RemoteUserActivityLogService : IUserActivityLogService, IDisposable
{
    private const string PendingKey = "habitinator_activity_pending_v1";
    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;

    private readonly IHttpClientFactory _http;
    private readonly IActivityStatisticsReader? _statsReader;
    private readonly IActivityEventStore? _eventStore;
    private readonly ILocalSettingsStore? _localStore;
    private readonly IClock? _clock;
    private readonly ILogger<RemoteUserActivityLogService>? _logger;
    private readonly SemaphoreSlim _queueGate = new(1, 1);

    public RemoteUserActivityLogService(
        IHttpClientFactory http,
        IActivityStatisticsReader? statsReader = null,
        IActivityEventStore? eventStore = null,
        ILocalSettingsStore? localStore = null,
        IClock? clock = null,
        ILogger<RemoteUserActivityLogService>? logger = null)
    {
        _http = http;
        _statsReader = statsReader;
        _eventStore = eventStore;
        _localStore = localStore;
        _clock = clock;
        _logger = logger;
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
                _logger?.LogDebug(ex, "Best-effort local activity store failed; remote will be attempted anyway.");
            }
        }

        await PostBestEffortAsync(
            new ActivityLogRequest(eventType, boardItemId, durationSeconds, itemTitleSnapshot, Guid.NewGuid()),
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
                _logger?.LogDebug(ex, "Best-effort local activity store failed; remote will be attempted anyway.");
            }
        }

        await PostBestEffortAsync(
            new ActivityLogRequest(ActivityEventType.TimerSession, boardItemId, sec, customLabel, Guid.NewGuid()),
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
        catch (Exception ex)
        {
            // Queue for later sync via outbox pattern. The request keeps its event id, so a replay
            // that already reached the server is deduplicated there.
            _logger?.LogDebug(ex, "Activity log post failed; queued for later sync.");
            await EnqueuePendingAsync(req, CancellationToken.None);
        }
    }

    private async Task EnqueuePendingAsync(ActivityLogRequest req, CancellationToken cancellationToken)
    {
        if (_localStore == null)
        {
            return;
        }

        await _queueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            _logger?.LogDebug(ex, "Enqueueing pending activity request failed; the event is dropped.");
        }
        finally
        {
            _queueGate.Release();
        }
    }

    public async Task TryFlushPendingAsync(CancellationToken cancellationToken = default)
    {
        if (_localStore == null)
        {
            return;
        }

        List<ActivityLogRequest> batch;
        await _queueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                var raw = _localStore.Read(PendingKey);
                if (string.IsNullOrEmpty(raw))
                {
                    return;
                }

                batch = JsonSerializer.Deserialize<List<ActivityLogRequest>>(raw, Serializer) ?? [];
                if (batch.Count == 0)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Pending activity queue is corrupted; treating as empty.");
                return;
            }

            // Take the batch out of the queue while it is in flight. Requests enqueued during the
            // flush land after it, and the failed tail is put back in front of them below.
            _localStore.Write(PendingKey, "");
        }
        finally
        {
            _queueGate.Release();
        }

        var firstFailedIndex = -1;
        for (var i = 0; i < batch.Count; i++)
        {
            try
            {
                using var res = await Client.PostAsJsonAsync("api/activity/log", batch[i], cancellationToken);
                res.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Activity flush failed; keeping this and following entries pending.");
                firstFailedIndex = i;
                break;
            }
        }

        if (firstFailedIndex != 0)
        {
            _statsReader?.InvalidateCache();
        }

        if (firstFailedIndex < 0)
        {
            return;
        }

        await _queueGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var raw = _localStore.Read(PendingKey);
            var newer = string.IsNullOrEmpty(raw)
                ? []
                : JsonSerializer.Deserialize<List<ActivityLogRequest>>(raw, Serializer) ?? [];
            var failed = batch.Skip(firstFailedIndex).ToList();
            failed.AddRange(newer);
            _localStore.Write(PendingKey, JsonSerializer.Serialize(failed, Serializer));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Persisting the remaining pending activity queue failed.");
        }
        finally
        {
            _queueGate.Release();
        }
    }

    public void Dispose() => _queueGate.Dispose();
}

public sealed record ActivityLogRequest(
    ActivityEventType EventType,
    Guid? BoardItemId,
    int? DurationSeconds,
    string? CustomLabel,
    Guid EventId = default);
