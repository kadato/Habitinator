namespace App.Shared.RCL.Services;

public sealed class RemoteBoardRefreshService : IRemoteBoardRefreshService
{
    private readonly object _lock = new();
    private readonly List<Func<Task>> _callbacks = [];

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
            foreach (Func<Task> fn in copy)
            {
                try
                {
                    await fn();
                }
                catch
                {
                    // best-effort; other subscribers still run
                }
            }
        }
    }
}
