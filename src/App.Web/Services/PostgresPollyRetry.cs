using Polly;
using Polly.Retry;

namespace App.Web.Services;

/// <summary>
/// Polly-based retries for operations that should not nest EF Core's execution strategy
/// (e.g. migrations run with a fresh <see cref="DbContext" /> on each attempt).
/// </summary>
public static class PostgresPollyRetry
{
    private static readonly ResiliencePipeline Shared = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            Delay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(16),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder().Handle<Exception>(PostgresTransientErrors.IsTransient),
        })
        .Build();

    /// <summary>Runs <paramref name="action" /> with exponential backoff, jitter, and transient-error filtering.</summary>
    public static ValueTask ExecuteAsync(Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default) =>
        Shared.ExecuteAsync(
            ct => new ValueTask(action(ct)),
            cancellationToken);
}
