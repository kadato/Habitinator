using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Board.Local;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Validation;

namespace App.Shared.RCL.Services.Remote;

public sealed class RemoteUserActivityLogService : IUserActivityLogService, IDisposable
{
    private const string PendingKey = "habitinator_activity_pending_v1";

    /// <summary>Number of pending events one flush sends at the same time. Each event carries its own id, so the server deduplicates a replay.</summary>
    private const int FlushConcurrency = 6;

    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;

    private readonly IHttpClientFactory _http;
    private readonly IActivityStatisticsReader? _statsReader;
    private readonly IActivityEventStore? _eventStore;
    private readonly ILocalSettingsStore? _localStore;
    private readonly IClock? _clock;
    private readonly ILogger<RemoteUserActivityLogService>? _logger;
    private readonly SemaphoreSlim _queueGate = new(1, 1);
    private readonly SemaphoreSlim _flushGate = new(1, 1);

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
            new ActivityLogRequest(eventType, boardItemId, durationSeconds, itemTitleSnapshot, Guid.NewGuid()));
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
            new ActivityLogRequest(ActivityEventType.TimerSession, boardItemId, sec, customLabel, Guid.NewGuid()));
    }

    private async Task PostBestEffortAsync(ActivityLogRequest req)
    {
        if (_localStore is null)
        {
            // No durable queue on this platform. Send in the background so the
            // caller is not held up by the network.
            _ = PostDirectInBackgroundAsync(req);
            return;
        }

        // Queue first so the event survives a crash, then drain in the
        // background. The caller returns without waiting for the network.
        await EnqueuePendingAsync(req, CancellationToken.None);
        if (!BoardSyncPausePreference.IsPausedIn(_localStore))
        {
            _ = FlushInBackgroundAsync();
        }
    }

    private async Task FlushInBackgroundAsync()
    {
        try
        {
            await TryFlushPendingAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Activity flush in the background failed. Keeping the queue.");
        }
    }

    private async Task PostDirectInBackgroundAsync(ActivityLogRequest req)
    {
        try
        {
            using var res = await Client.PostAsJsonAsync("api/activity/log", req, CancellationToken.None);
            res.EnsureSuccessStatusCode();
            _statsReader?.InvalidateCache();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Activity log post failed and no local queue is available.");
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

    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        if (_localStore == null)
        {
            return 0;
        }

        await _queueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                var raw = _localStore.Read(PendingKey);
                if (string.IsNullOrEmpty(raw))
                {
                    return 0;
                }

                var list = JsonSerializer.Deserialize<List<ActivityLogRequest>>(raw, Serializer);
                return list?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }
        finally
        {
            _queueGate.Release();
        }
    }

    public async Task TryFlushPendingAsync(CancellationToken cancellationToken = default)
    {
        await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FlushPendingCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _flushGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Shutdown disposed the gate while a flush was running.
            }
        }
    }

    private async Task FlushPendingCoreAsync(CancellationToken cancellationToken)
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

        // Send the backlog together. Sending one request per event would make a
        // long offline queue wait minutes on the first sync.
        var results = new bool[batch.Count];
        var nextIndex = -1;
        var stop = 0;

        async Task SendOneAsync()
        {
            while (Volatile.Read(ref stop) == 0)
            {
                var i = Interlocked.Increment(ref nextIndex);
                if (i >= batch.Count)
                {
                    return;
                }

                try
                {
                    using var res = await Client.PostAsJsonAsync("api/activity/log", batch[i], cancellationToken);
                    res.EnsureSuccessStatusCode();
                    results[i] = true;
                }
                catch (Exception ex)
                {
                    results[i] = false;
                    Interlocked.Exchange(ref stop, 1);
                    _logger?.LogDebug(ex, "Activity flush failed. Keeping this entry and the unsent entries pending.");
                    return;
                }
            }
        }

        var workers = Math.Min(FlushConcurrency, batch.Count);
        await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => SendOneAsync()));

        if (results.Any(success => success))
        {
            _statsReader?.InvalidateCache();
        }

        if (results.All(success => success))
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
            var failed = new List<ActivityLogRequest>();
            for (var i = 0; i < batch.Count; i++)
            {
                if (!results[i])
                {
                    failed.Add(batch[i]);
                }
            }

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

    public void Dispose()
    {
        _queueGate.Dispose();
        _flushGate.Dispose();
    }
}

[ValidatableType]
public sealed record ActivityLogRequest(
    ActivityEventType EventType,
    Guid? BoardItemId,
    [Range(0, 86400)] int? DurationSeconds,
    [StringLength(200)] string? CustomLabel,
    Guid EventId = default);
