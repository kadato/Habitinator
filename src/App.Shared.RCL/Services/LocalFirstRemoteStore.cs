using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

/// <summary>
///     Coordinates local-first reads with best-effort background refreshes from a remote
///     source. Serializes local writes so a stale background fetch can never overwrite
///     a newer local edit, and observes background failures instead of swallowing them.
/// </summary>
public sealed class LocalFirstRemoteStore<T> : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<string, T> _readLocal;
    private readonly Action<string, T> _writeLocal;
    private readonly Func<T, string> _serialize;
    private readonly ILogger _logger;

    public LocalFirstRemoteStore(
        Func<string, T> readLocal,
        Action<string, T> writeLocal,
        Func<T, string> serialize,
        ILogger logger)
    {
        _readLocal = readLocal;
        _writeLocal = writeLocal;
        _serialize = serialize;
        _logger = logger;
    }

    public T GetLocal(string key)
    {
        return _readLocal(key);
    }

    /// <summary>Persists locally, serialized with other local writes.</summary>
    public async Task WriteLocalAsync(string key, T value, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _writeLocal(key, value);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Fetches from the remote in the background and applies the result locally only
    ///     if the local copy has not changed since the fetch started.
    /// </summary>
    public void RefreshInBackground(
        string key,
        T local,
        Func<CancellationToken, Task<T?>> fetchRemote,
        Action onApplied,
        CancellationToken cancellationToken)
    {
        var capturedJson = _serialize(local);
        _ = RefreshAsync(key, capturedJson, fetchRemote, onApplied, cancellationToken);
    }

    private async Task RefreshAsync(
        string key,
        string capturedJson,
        Func<CancellationToken, Task<T?>> fetchRemote,
        Action onApplied,
        CancellationToken cancellationToken)
    {
        try
        {
            var remote = await fetchRemote(cancellationToken).ConfigureAwait(false);
            if (remote is null)
            {
                return;
            }

            var remoteJson = _serialize(remote);
            if (remoteJson == capturedJson)
            {
                return;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_serialize(_readLocal(key)) != capturedJson)
                {
                    return;
                }

                _writeLocal(key, remote);
            }
            finally
            {
                _gate.Release();
            }

            onApplied();
        }
        catch (OperationCanceledException)
        {
            // Shutdown or cancellation; nothing to do.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Background refresh of local-first settings failed.");
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
