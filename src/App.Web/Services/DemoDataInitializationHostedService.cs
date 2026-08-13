using Microsoft.Extensions.Options;

namespace App.Web.Services;

/// <summary>
/// Runs EF migrations and demo seed after the web host starts listening so Azure App Service warmup,
/// the /health probe, is not blocked by long-running seed work such as the guest activity heatmap.
/// </summary>
public sealed class DemoDataInitializationHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<DemoDataInitializationHostedService> _logger;
    private readonly IOptions<DemoInitializationOptions> _options;

    public DemoDataInitializationHostedService(
        IServiceProvider services,
        IHostApplicationLifetime lifetime,
        ILogger<DemoDataInitializationHostedService> logger,
        IOptions<DemoInitializationOptions> options)
    {
        _services = services;
        _lifetime = lifetime;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnStarted() => tcs.TrySetResult();
            using var registration = _lifetime.ApplicationStarted.Register(OnStarted);
            await tcs.Task.WaitAsync(stoppingToken).ConfigureAwait(false);

            var maxAttempts = Math.Max(1, _options.Value.MaxAttempts);
            var delay = TimeSpan.FromSeconds(Math.Max(1, _options.Value.RetryDelaySeconds));

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await DemoDataSeeder.SeedAsync(_services, stoppingToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    _logger.LogWarning(ex,
                        "Database not ready for migration/seeding (attempt {Attempt}/{Max}). Retrying after delay…",
                        attempt, maxAttempts);
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    _logger.LogError(ex,
                        "Demo data seeding failed after {Max} attempts. Demo guest may be unavailable until the DB is reachable. " +
                        "Check ConnectionStrings:DefaultConnection.",
                        maxAttempts);
                    return;
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "Demo data seeding was canceled because the application host is shutting down.");
        }
    }
}

public sealed class DemoInitializationOptions
{
    public const string SectionName = "DemoInitialization";

    /// <summary>Retries when Postgres is waking on Neon or transiently unavailable.</summary>
    public int MaxAttempts { get; set; } = 6;

    public int RetryDelaySeconds { get; set; } = 5;
}
