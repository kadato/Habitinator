using System.Text.Json;

using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;

using Microsoft.JSInterop;

namespace App.Web.Client.Services;

/// <summary>WASM local-first store. Reads come from an in-memory mirror. Writes persist to IndexedDB in the background.</summary>
#pragma warning disable CA1001 // DI scoped: owns long-lived semaphores released with the app scope.
public sealed class IndexedDbBoardLocalStore : IBoardLocalStore
{
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = SharedJsonSerializerContext.Default
    };

    private readonly InMemoryBoardLocalStore _inner = new();
    private readonly IJSRuntime _js;
    private readonly ILogger<IndexedDbBoardLocalStore> _logger;
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private bool _loaded;
    private CancellationTokenSource? _pendingPersist;

    public IndexedDbBoardLocalStore(IJSRuntime js, ILogger<IndexedDbBoardLocalStore> logger)
    {
        _js = js;
        _logger = logger;
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return;
        }

        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            if (_loaded)
            {
                return;
            }

            try
            {
                await _js.InvokeVoidAsync("habitinatorLoadScript", "_content/App.Shared.RCL/js/boardLocalStore.js");
            }
            catch
            {
                // Prerender or loader unavailable. Memory mirror still works.
            }

            try
            {
                var raw = await _js.InvokeAsync<string?>("habBoardStore.load", cancellationToken);
                if (!string.IsNullOrEmpty(raw))
                {
                    var state = JsonSerializer.Deserialize<BoardLocalState>(raw, Serializer);
                    if (state is not null)
                    {
                        _inner.ImportState(state);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "IndexedDB board load skipped. Memory mirror still works.");
            }

            _loaded = true;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public bool IsOnline()
    {
        try
        {
            if (_js is IJSInProcessRuntime sync)
            {
                return sync.Invoke<bool>("habBoardStore.isOnline");
            }
        }
        catch
        {
            // Prerender or JS unavailable. Assume online so sync can try.
        }

        return true;
    }

    public Task<BoardStoreMeta> GetMetaAsync(CancellationToken cancellationToken = default) =>
        _inner.GetMetaAsync(cancellationToken);

    public Task SetMetaAsync(BoardStoreMeta meta, CancellationToken cancellationToken = default)
    {
        var task = _inner.SetMetaAsync(meta, cancellationToken);
        SchedulePersist();
        return task;
    }

    public Task<IReadOnlyList<BoardLocalRow>> ListItemsAsync(string userKey, bool includeArchived, CancellationToken cancellationToken = default) =>
        _inner.ListItemsAsync(userKey, includeArchived, cancellationToken);

    public Task<BoardLocalRow?> FindItemAsync(string userKey, Guid id, CancellationToken cancellationToken = default) =>
        _inner.FindItemAsync(userKey, id, cancellationToken);

    public Task UpsertItemAsync(BoardLocalRow row, CancellationToken cancellationToken = default)
    {
        var task = _inner.UpsertItemAsync(row, cancellationToken);
        SchedulePersist();
        return task;
    }

    public Task DeleteItemAsync(string userKey, Guid id, CancellationToken cancellationToken = default)
    {
        var task = _inner.DeleteItemAsync(userKey, id, cancellationToken);
        SchedulePersist();
        return task;
    }

    public Task DeleteAllItemsAsync(string userKey, CancellationToken cancellationToken = default)
    {
        var task = _inner.DeleteAllItemsAsync(userKey, cancellationToken);
        SchedulePersist();
        return task;
    }

    public Task ReplaceAllItemsAsync(string userKey, IReadOnlyList<BoardLocalRow> rows, string? cursor, CancellationToken cancellationToken = default)
    {
        var task = _inner.ReplaceAllItemsAsync(userKey, rows, cursor, cancellationToken);
        SchedulePersist();
        return task;
    }

    public Task<IReadOnlyList<BoardOutboxEntry>> ListOutboxAsync(string userKey, CancellationToken cancellationToken = default) =>
        _inner.ListOutboxAsync(userKey, cancellationToken);

    public Task<BoardOutboxEntry?> FindOutboxAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        _inner.FindOutboxAsync(operationId, cancellationToken);

    public Task EnqueueOutboxAsync(BoardOutboxEntry entry, CancellationToken cancellationToken = default)
    {
        var task = _inner.EnqueueOutboxAsync(entry, cancellationToken);
        SchedulePersist();
        return task;
    }

    public Task UpdateOutboxAsync(BoardOutboxEntry entry, CancellationToken cancellationToken = default)
    {
        var task = _inner.UpdateOutboxAsync(entry, cancellationToken);
        SchedulePersist();
        return task;
    }

    public Task DeleteOutboxAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var task = _inner.DeleteOutboxAsync(operationId, cancellationToken);
        SchedulePersist();
        return task;
    }

    public Task ClearOutboxForUserAsync(string userKey, CancellationToken cancellationToken = default)
    {
        var task = _inner.ClearOutboxForUserAsync(userKey, cancellationToken);
        SchedulePersist();
        return task;
    }

    public Task<bool> HasOutboxAsync(string userKey, CancellationToken cancellationToken = default) =>
        _inner.HasOutboxAsync(userKey, cancellationToken);

    public Task ClearAllStateAsync(CancellationToken cancellationToken = default)
    {
        var task = _inner.ClearAllStateAsync(cancellationToken);
        SchedulePersist();
        return task;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var pending = Interlocked.Exchange(ref _pendingPersist, null);
        if (pending is not null)
        {
            try
            {
                await pending.CancelAsync();
            }
            catch
            {
                // Ignore cancel races on rapid writes.
            }

            pending.Dispose();
        }

        await PersistNowAsync(cancellationToken);
    }

    private void SchedulePersist()
    {
        try
        {
            _pendingPersist?.Cancel();
            _pendingPersist?.Dispose();
        }
        catch
        {
            // Ignore cancel races on rapid writes.
        }

        var cts = new CancellationTokenSource();
        _pendingPersist = cts;
        _ = PersistAfterDelayAsync(TimeSpan.FromMilliseconds(250), cts.Token);
    }

    private async Task PersistAfterDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await PersistNowAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // A newer write superseded this persist.
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "IndexedDB board persist failed.");
        }
    }

    private async Task PersistNowAsync(CancellationToken cancellationToken)
    {
        await _persistGate.WaitAsync(cancellationToken);
        try
        {
            var state = _inner.ExportState();
            var raw = JsonSerializer.Serialize(state, Serializer);
            await _js.InvokeVoidAsync("habBoardStore.save", cancellationToken, raw);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "IndexedDB board persist failed.");
        }
        finally
        {
            _persistGate.Release();
        }
    }
}
