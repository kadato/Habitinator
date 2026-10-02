using System.Threading.Channels;

using App.Shared.RCL.Services.Remote;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services.Board.Local;

/// <summary>Shared periodic drain and pull. Resume, hub events, visibility changes, and a timer trigger it.</summary>
public sealed partial class BoardSyncCoordinator : IBoardSyncRequestor, IDisposable
{
    private const int StuckAfterAttempts = 8;

    private readonly LocalFirstBoardDataService _board;
    private readonly ICurrentUserKeyProvider _users;
    private readonly BoardSyncStatus _status;
    private readonly BoardInitialLoadSignal _initialLoad;
    private readonly RemoteBoardRefreshService _refresh;
    private readonly ILogger<BoardSyncCoordinator> _logger;
    private readonly IServiceProvider _services;
    private readonly Func<bool> _isOfflineProbe;
    private readonly SemaphoreSlim _run = new(1, 1);
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(45));
    private readonly CancellationTokenSource _appStopping = new();
    private readonly Channel<bool> _syncChannel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    });
    private bool _disposed;

    public BoardSyncCoordinator(
        LocalFirstBoardDataService board,
        ICurrentUserKeyProvider users,
        BoardSyncStatus status,
        BoardInitialLoadSignal initialLoad,
        RemoteBoardRefreshService refresh,
        ILogger<BoardSyncCoordinator> logger,
        IServiceProvider services,
        Func<bool>? isOfflineProbe = null)
    {
        _board = board;
        _users = users;
        _status = status;
        _initialLoad = initialLoad;
        _refresh = refresh;
        _logger = logger;
        _services = services;
        _isOfflineProbe = isOfflineProbe ?? (() => false);
        _ = Task.Run(() => PeriodicLoopAsync(_appStopping.Token), _appStopping.Token);
    }

    public void RequestSync(bool notifyOnProgress = true)
    {
        _syncChannel.Writer.TryWrite(notifyOnProgress);
    }

    public async Task RunPullAndDrainAsync(bool notifyOnProgress, CancellationToken cancellationToken)
    {
        await _run.WaitAsync(cancellationToken);
        try
        {
            await RunPullAndDrainCoreAsync(notifyOnProgress, cancellationToken);
        }
        finally
        {
            try
            {
                _run.Release();
            }
            catch (ObjectDisposedException)
            {
                // Shutdown disposed the semaphore while this run was in flight.
            }
        }
    }

    private async Task RunPullAndDrainCoreAsync(bool notifyOnProgress, CancellationToken cancellationToken)
    {
        _status.SetSyncing(true);
        try
        {
            _status.RefreshConnectivity(_isOfflineProbe);
            if (_status.IsOffline)
            {
                _status.SetProblem("Offline - board changes stay on this device until you reconnect.");
                await RefreshPendingCountAsync(cancellationToken);
                return;
            }

            if (!await _users.HasAuthAsync(cancellationToken))
            {
                return;
            }

            var progressed = false;
            while (await _board.TryDrainOneOutboxOperationAsync(cancellationToken))
            {
                progressed = true;
            }

            if (await _board.TryPullRemoteMirrorAsync(cancellationToken))
            {
                progressed = true;
            }

            if (progressed)
            {
                _status.SetLastSynced(DateTimeOffset.UtcNow);
                if (notifyOnProgress)
                {
                    _ = _refresh.NotifyFromRemoteAsync(cancellationToken);
                }
            }

            var stuck = await _board.TryGetStuckOutboxHintAsync(StuckAfterAttempts, cancellationToken);
            _status.SetProblem(stuck);
            await RefreshPendingCountAsync(cancellationToken);

            if (progressed)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var stats = scope.ServiceProvider.GetService<IActivityStatisticsReader>();
                    stats?.InvalidateCache();
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Could not invalidate stats cache after sync.");
                }
            }

            try
            {
                using var scope = _services.CreateScope();
                if (scope.ServiceProvider.GetService<IUserActivityLogService>() is RemoteUserActivityLogService remoteFlush)
                {
                    await remoteFlush.TryFlushPendingAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Activity pending flush failed.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Board sync tick failed.");
            _status.SetProblem("Could not reach the server. Board changes will sync when connection is restored.");
        }
        finally
        {
            _status.SetSyncing(false);
        }
    }

    private async Task RefreshPendingCountAsync(CancellationToken cancellationToken)
    {
        try
        {
            _status.SetPendingCount(await _board.GetPendingOutboxCountAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Could not refresh sync pending count.");
        }
    }

    private async Task PeriodicLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_initialLoad.IsComplete)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnCompleted(object? sender, EventArgs e) => tcs.TrySetResult();
                _initialLoad.Completed += OnCompleted;
                try
                {
                    if (!_initialLoad.IsComplete)
                    {
                        await tcs.Task.WaitAsync(cancellationToken);
                    }
                }
                finally
                {
                    _initialLoad.Completed -= OnCompleted;
                }
            }

            // Pull once right after the first board load so remote changes and the
            // sync status show up without waiting for the first timer tick.
            try
            {
                await RunPullAndDrainAsync(notifyOnProgress: true, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Initial board sync failed.");
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var notifyOnProgress = true;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var channelTask = _syncChannel.Reader.ReadAsync(cts.Token).AsTask();
                var timerTask = _timer.WaitForNextTickAsync(cts.Token).AsTask();
                var completed = await Task.WhenAny(channelTask, timerTask).ConfigureAwait(false);
                await cts.CancelAsync().ConfigureAwait(false);

                if (completed == channelTask)
                {
                    notifyOnProgress = await channelTask.ConfigureAwait(false);
                }

                try
                {
                    await RunPullAndDrainAsync(notifyOnProgress, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Periodic or requested board sync failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _appStopping.Cancel();
        _appStopping.Dispose();
        _timer.Dispose();
        _run.Dispose();
    }
}
