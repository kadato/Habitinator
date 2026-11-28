using Microsoft.Extensions.Options;

namespace App.Web.Services;

/// <summary>Removes expired per-visitor demo boards in the background.</summary>
public sealed class DemoGuestCloneCleanupHostedService(
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    IOptions<DemoGuestCloneOptions> options,
    ILogger<DemoGuestCloneCleanupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnStarted() => started.TrySetResult();

            using var registration = lifetime.ApplicationStarted.Register(OnStarted);
            await started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);

            var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.CleanupIntervalMinutes));
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await CleanupOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation(ex, "Demo guest cleanup stopped because the application host is shutting down.");
        }
    }

    private async Task CleanupOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var clones = scope.ServiceProvider.GetRequiredService<DemoGuestCloneService>();
            await clones.CleanupExpiredAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Demo guest cleanup failed. It will retry on the next pass.");
        }
    }
}
