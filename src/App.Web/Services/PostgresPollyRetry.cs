using Polly;
using Polly.Retry;

namespace App.Web.Services;

/// <summary>
/// Polly-based retries for operations that should not nest EF Core's execution strategy.
/// Migrations run with a fresh <see cref="DbContext" /> on each try.
/// </summary>
public static class PostgresPollyRetry
{
    private static ResiliencePipeline CreatePipeline(ILogger? logForRetries)
    {
        var options = new RetryStrategyOptions
        {
            MaxRetryAttempts = PostgresRetryDefaults.RetryMaxCount,
            Delay = TimeSpan.FromSeconds(1),
            MaxDelay = PostgresRetryDefaults.RetryMaxDelay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder().Handle<Exception>(PostgresTransientErrors.IsTransient),
        };

        if (logForRetries is not null)
        {
            options.OnRetry = args =>
            {
                if (args.Outcome.Exception is { } ex)
                {
                    logForRetries.LogWarning(
                        ex,
                        "Transient database error; Polly will retry (attempt {Attempt}).",
                        args.AttemptNumber);
                }

                return default;
            };
        }

        return new ResiliencePipelineBuilder()
            .AddRetry(options)
            .Build();
    }

    /// <summary>Runs <paramref name="action" /> with exponential backoff, jitter, transient-error filtering, and per-try logging.</summary>
    public static ValueTask ExecuteAsync(Func<CancellationToken, Task> action, ILogger logForRetries,
        CancellationToken cancellationToken = default) =>
        CreatePipeline(logForRetries).ExecuteAsync(
            ct => new ValueTask(action(ct)),
            cancellationToken);
}
