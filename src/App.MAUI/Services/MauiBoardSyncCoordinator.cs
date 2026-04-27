using App.MAUI.Services.LocalBoard;

using Microsoft.Extensions.Logging;

namespace App.MAUI.Services;

/// <summary>Periodic drain + pull; also invoked on hub/visibility refresh and app resume.</summary>
public sealed class MauiBoardSyncCoordinator
{
    private const int StuckAfterAttempts = 8;

    private readonly LocalFirstBoardDataService _board;
    private readonly IAuthTokenStore _tokens;
    private readonly MauiBoardSyncStatus _status;
    private readonly ILogger<MauiBoardSyncCoordinator> _logger;
    private readonly SemaphoreSlim _run = new(1, 1);
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(45));
    private readonly CancellationTokenSource _appStopping = new();

    public MauiBoardSyncCoordinator(
        LocalFirstBoardDataService board,
        IAuthTokenStore tokens,
        MauiBoardSyncStatus status,
        ILogger<MauiBoardSyncCoordinator> logger)
    {
        _board = board;
        _tokens = tokens;
        _status = status;
        _logger = logger;
        _ = Task.Run(() => PeriodicLoopAsync(_appStopping.Token));
    }

    /// <summary>Fire-and-forget sync (resume / post-login).</summary>
    public void RequestSync()
    {
        _ = RunPullAndDrainAsync(CancellationToken.None);
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
            _run.Release();
        }
    }

    private async Task RunPullAndDrainCoreAsync(CancellationToken cancellationToken)
    {
        _status.RefreshConnectivity();
        if (_status.IsOffline)
        {
            _status.SyncProblemMessage = "Offline — board changes stay on this device until you reconnect.";
            return;
        }

        if (string.IsNullOrEmpty(await _tokens.GetAccessTokenAsync(cancellationToken)))
            return;

        _status.SyncProblemMessage = null;
        _status.IsSyncing = true;
        try
        {
            var progressed = false;
            while (await _board.TryDrainOneOutboxOperationAsync(cancellationToken))
                progressed = true;

            if (await _board.TryPullRemoteMirrorAsync(cancellationToken))
                progressed = true;

            if (progressed) _status.LastSyncedUtc = DateTimeOffset.UtcNow;

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
