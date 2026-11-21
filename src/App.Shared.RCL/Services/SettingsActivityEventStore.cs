using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

public sealed class SettingsActivityEventStore : IActivityEventStore, IDisposable
{
    private const string EventsKey = "habitinator_activity_events_v1";
    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;
    private readonly ILocalSettingsStore? _store;
    private readonly ILogger<SettingsActivityEventStore>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<UserActivityEventRecord>? _memory;
    private int _saveVersion;
    private bool _disposed;

    public SettingsActivityEventStore(
        ILocalSettingsStore? store = null,
        ILogger<SettingsActivityEventStore>? logger = null)
    {
        _store = store;
        _logger = logger;
    }

    public event EventHandler<UserActivityEventRecord>? Appended;

    public async Task AppendAsync(UserActivityEventRecord record, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await LoadAsync(cancellationToken);
            list.Add(record);
            // Keep bounded to last 5000 events to avoid storage bloat
            if (list.Count > 5000)
            {
                list.RemoveRange(0, list.Count - 5000);
            }

            // Coalesce rapid appends into one write. Serializing the whole log
            // on every habit tap costs UI time once the log grows.
            ScheduleSave();
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            Appended?.Invoke(this, record);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Activity event subscriber failed; continuing with remaining subscribers.");
        }
    }

    public async Task<IReadOnlyList<UserActivityEventRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await LoadAsync(cancellationToken);
            return [.. list];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<UserActivityEventRecord>> GetInRangeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return [.. all.Where(e => e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc)];
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Increment(ref _saveVersion);
            _memory = [];
            if (_store != null)
            {
                try
                {
                    _store.Write(EventsKey, "");
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Clearing persisted activity events failed.");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<List<UserActivityEventRecord>> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_memory != null)
        {
            return Task.FromResult(_memory);
        }

        if (_store == null)
        {
            _memory = [];
            return Task.FromResult(_memory);
        }

        string? raw = null;
        try
        {
            raw = _store.Read(EventsKey);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Reading persisted activity events failed; treating as empty.");
        }

        if (string.IsNullOrEmpty(raw))
        {
            _memory = [];
            return Task.FromResult(_memory);
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<List<UserActivityEventRecord>>(raw, Serializer);
            _memory = deserialized ?? [];
            return Task.FromResult(_memory);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Persisted activity events are corrupted; resetting to empty.");
            _memory = [];
            return Task.FromResult(_memory);
        }
    }

    private void ScheduleSave()
    {
        var version = Interlocked.Increment(ref _saveVersion);
        _ = SaveAfterDelayAsync(version);
    }

    private async Task SaveAfterDelayAsync(int version)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed || version != Volatile.Read(ref _saveVersion) || _memory is null || _store is null)
                {
                    return;
                }

                try
                {
                    var json = JsonSerializer.Serialize(_memory, Serializer);
                    _store.Write(EventsKey, json);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Persisting activity events failed. Keeping the in-memory copy.");
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // Shutdown disposed the gate before the delayed save ran.
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Delayed activity event save failed.");
        }
    }
}
