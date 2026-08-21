using System.Threading.Channels;

using App.MAUI.Services.LocalBoard;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Remote;

#pragma warning disable IDE0005 // Using is required for IServiceProvider.CreateScope extension method, analyzer incorrectly reports as unnecessary
using Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0005
using Microsoft.Extensions.Logging;

namespace App.MAUI.Services;

/// <summary>Periodic drain + pull. Also invoked on hub/visibility refresh and app resume.</summary>
#pragma warning disable CA1001, S2930 // DI singleton: owns long-lived disposable state and is never disposed by the container.
public sealed partial class MauiBoardSyncCoordinator
{
    private const int StuckAfterAttempts = 8;

    private readonly LocalFirstBoardDataService _board;
    private readonly IAuthTokenStore _tokens;
    private readonly MauiBoardSyncStatus _status;
    private readonly MauiInitialBoardLoadSignal _initialLoad;
    private readonly RemoteBoardRefreshService _refresh;
    private readonly ILogger<MauiBoardSyncCoordinator> _logger;
    private readonly IServiceProvider _services;
    private readonly SemaphoreSlim _run = new(1, 1);
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(45));
    private readonly CancellationTokenSource _appStopping = new();
    private readonly Channel<bool> _syncChannel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    });

    public MauiBoardSyncCoordinator(
        LocalFirstBoardDataService board,
        IAuthTokenStore tokens,
        MauiBoardSyncStatus status,
        MauiInitialBoardLoadSignal initialLoad,
        RemoteBoardRefreshService refresh,
        ILogger<MauiBoardSyncCoordinator> logger,
        IServiceProvider services)
    {
        _board = board;
        _tokens = tokens;
        _status = status;
        _initialLoad = initialLoad;
        _refresh = refresh;
        _logger = logger;
        _services = services;
        _ = Task.Run(() => PeriodicLoopAsync(_appStopping.Token), _appStopping.Token);
    }

    /// <summary>Fire-and-forget sync on resume or after login.</summary>
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
        // Publish the flag before any I/O so concurrent readers like empty-board
        // fetch-on-read do not race a run that is about to touch the database.
        _status.IsSyncing = true;
        try
        {
            _status.RefreshConnectivity();
            if (_status.IsOffline)
            {
                _status.SyncProblemMessage = "Offline - board changes stay on this device until you reconnect.";
                return;
            }

            if (string.IsNullOrEmpty(await _tokens.GetAccessTokenAsync(cancellationToken)))
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

            if (progressed && notifyOnProgress)
            {
                _status.LastSyncedUtc = DateTimeOffset.UtcNow;
                _ = _refresh.NotifyFromRemoteAsync(cancellationToken);
            }
            else if (progressed)
            {
                _status.LastSyncedUtc = DateTimeOffset.UtcNow;
            }

            var stuck = await _board.TryGetStuckOutboxHintAsync(StuckAfterAttempts, cancellationToken);
            _status.SyncProblemMessage = stuck;

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

            // Flush pending activity logs via outbox pattern when online
            try
            {
                using var scope = _services.CreateScope();
                if (scope.ServiceProvider.GetService<IUserActivityLogService>() is RemoteUserActivityLogService remoteLog)
                {
                    await remoteLog.TryFlushPendingAsync(cancellationToken);
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
            _status.SyncProblemMessage = "Could not reach the server. Board changes will sync when connection is restored.";
        }
        finally
        {
            _status.IsSyncing = false;
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
            // shutdown
        }
    }
}
#pragma warning restore CA1001, S2930
