using App.MAUI.Services.LocalBoard;
using App.Shared.RCL.Services;

using Microsoft.Extensions.Logging;

namespace App.MAUI.Services;

/// <summary>Periodic drain + pull; also invoked on hub/visibility refresh and app resume.</summary>
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
    private readonly SemaphoreSlim _run = new(1, 1);
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(45));
    private readonly CancellationTokenSource _appStopping = new();

    public MauiBoardSyncCoordinator(
        LocalFirstBoardDataService board,
        IAuthTokenStore tokens,
        MauiBoardSyncStatus status,
        MauiInitialBoardLoadSignal initialLoad,
        RemoteBoardRefreshService refresh,
        ILogger<MauiBoardSyncCoordinator> logger)
    {
        _board = board;
        _tokens = tokens;
        _status = status;
        _initialLoad = initialLoad;
        _refresh = refresh;
        _logger = logger;
        _ = Task.Run(() => PeriodicLoopAsync(_appStopping.Token), _appStopping.Token);
    }

    /// <summary>Fire-and-forget sync (resume / post-login).</summary>
    public void RequestSync()
    {
        _ = RunPullAndDrainAsync(_appStopping.Token);
    }

    public async Task RunPullAndDrainAsync(CancellationToken cancellationToken)
    {
        await _run.WaitAsync(cancellationToken);
        try
        {
            await RunPullAndDrainCoreAsync(cancellationToken);
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

    private async Task RunPullAndDrainCoreAsync(CancellationToken cancellationToken)
    {
        // Publish the flag before any I/O so concurrent readers (e.g. empty-board
        // fetch-on-read) do not race a run that is about to touch the database.
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

            _status.SyncProblemMessage = null;

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
                _status.LastSyncedUtc = DateTimeOffset.UtcNow;
                _ = _refresh.NotifyFromRemoteAsync(cancellationToken);
            }

            var stuck = await _board.TryGetStuckOutboxHintAsync(StuckAfterAttempts, cancellationToken);
            _status.SyncProblemMessage = stuck;
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

            while (await _timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await RunPullAndDrainAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Periodic board sync failed.");
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
