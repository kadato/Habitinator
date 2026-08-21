using System.Text.Json;

namespace App.Shared.RCL.Services;

public sealed class SettingsActivityEventStore : IActivityEventStore, IDisposable
{
    private const string EventsKey = "habitinator_activity_events_v1";
    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;
    private readonly ILocalSettingsStore? _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<UserActivityEventRecord>? _memory;
    private bool _disposed;

    public SettingsActivityEventStore(ILocalSettingsStore? store = null)
    {
        _store = store;
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

            await SaveAsync(list, cancellationToken);
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
            // Ignore - best effort event notification, subscribers should handle their own errors
            _ = ex;
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
            _memory = [];
            if (_store != null)
            {
                try
                {
                    _store.Write(EventsKey, "");
                }
                catch (Exception ex)
                {
                    // Ignore - best effort to clear persisted store
                    _ = ex;
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
            // Ignore - best effort read from store, treat as empty
            _ = ex;
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
            // Ignore - corrupted data, reset to empty
            _ = ex;
            _memory = [];
            return Task.FromResult(_memory);
        }
    }

    private Task SaveAsync(List<UserActivityEventRecord> list, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _memory = list;
        if (_store != null)
        {
            try
            {
                var json = JsonSerializer.Serialize(list, Serializer);
                _store.Write(EventsKey, json);
            }
            catch (Exception ex)
            {
                // Ignore - best effort write to store
                _ = ex;
            }
        }

        return Task.CompletedTask;
    }
}
