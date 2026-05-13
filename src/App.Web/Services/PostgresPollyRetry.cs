using Microsoft.Extensions.Logging;

using Polly;
using Polly.Retry;

namespace App.Web.Services;

/// <summary>
/// Polly-based retries for operations that should not nest EF Core's execution strategy
/// (e.g. migrations run with a fresh <see cref="DbContext" /> on each attempt).
/// </summary>
public static class PostgresPollyRetry
{
    private static readonly ResiliencePipeline Shared = CreatePipeline(logForRetries: null);

    private static ResiliencePipeline CreatePipeline(ILogger? logForRetries)
    {
        var options = new RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            Delay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(16),
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

    /// <summary>Runs <paramref name="action" /> with exponential backoff, jitter, and transient-error filtering.</summary>
    public static ValueTask ExecuteAsync(Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default) =>
        Shared.ExecuteAsync(
            ct => new ValueTask(action(ct)),
            cancellationToken);

    /// <inheritdoc cref="ExecuteAsync(System.Func{System.Threading.CancellationToken,System.Threading.Tasks.Task},System.Threading.CancellationToken)" />
    /// <remarks>Logs each retry at warning level when Polly is about to wait and retry.</remarks>
    public static ValueTask ExecuteAsync(Func<CancellationToken, Task> action, ILogger logForRetries,
        CancellationToken cancellationToken = default) =>
        CreatePipeline(logForRetries).ExecuteAsync(
            ct => new ValueTask(action(ct)),
            cancellationToken);
}
