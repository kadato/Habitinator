using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

public sealed class RemoteBoardRefreshService(ILogger<RemoteBoardRefreshService>? logger = null) : IRemoteBoardRefreshService
{
    private readonly List<Func<Task>> _callbacks = [];
    private readonly System.Threading.Lock _lock = new();

    public void RegisterForRemoteRefresh(Func<Task> onRefresh)
    {
        lock (_lock)
        {
            if (!_callbacks.Contains(onRefresh))
            {
                _callbacks.Add(onRefresh);
            }
        }
    }

    public void UnregisterForRemoteRefresh(Func<Task> onRefresh)
    {
        lock (_lock)
        {
            _ = _callbacks.RemoveAll(r => ReferenceEquals(r, onRefresh));
        }
    }

    public Task NotifyFromRemoteAsync(CancellationToken cancellationToken = default)
    {
        List<Func<Task>> copy;
        lock (_lock)
        {
            copy = _callbacks.ToList();
        }

        if (copy.Count == 0)
        {
            return Task.CompletedTask;
        }

        return RunSequentiallyAsync();

        async Task RunSequentiallyAsync()
        {
            foreach (var fn in copy)
            {
                try
                {
                    await fn();
                }
                catch (Exception ex)
                {
                    // Keep going. One bad subscriber must not block the others.
                    logger?.LogDebug(ex, "A board refresh subscriber failed.");
                }
            }
        }
    }
}
