using App.Web.Services;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace App.Web.IntegrationTests;

public sealed class PostgresResilienceConnectionStringTests
{
    [Fact]
    public void EnsureColdStartTimeouts_raises_low_timeout_to_minimum()
    {
        var raw = "Host=localhost;Port=5432;Database=x;Username=u;Password=p;Timeout=3";
        var enriched = PostgresResilienceConnectionString.EnsureColdStartTimeouts(raw);

        enriched.Should().Contain($"Timeout={PostgresResilienceConnectionString.MinimumConnectionTimeoutSeconds}");
    }

    [Fact]
    public void EnsureColdStartTimeouts_preserves_timeout_when_already_high()
    {
        var raw = "Host=localhost;Port=5432;Database=x;Username=u;Password=p;Timeout=30";
        var enriched = PostgresResilienceConnectionString.EnsureColdStartTimeouts(raw);

        enriched.Should().Contain("Timeout=30");
    }
}

public sealed class PostgresTransientErrorsTests
{
    [Fact]
    public void IsTransient_true_for_neon_style_message_on_npgsql_exception()
    {
        var ex = new NpgsqlException("Couldn't connect to compute node");

        PostgresTransientErrors.IsTransient(ex).Should().BeTrue();
    }

    [Fact]
    public void IsTransient_true_for_db_update_exception_wrapping_npgsql()
    {
        var inner = new NpgsqlException("Connection terminated unexpectedly");
        var ex = new Microsoft.EntityFrameworkCore.DbUpdateException("update failed", inner);

        PostgresTransientErrors.IsTransient(ex).Should().BeTrue();
    }

    [Fact]
    public void IsTransient_false_for_unrelated_exception()
    {
        PostgresTransientErrors.IsTransient(new InvalidOperationException("bad data")).Should().BeFalse();
    }

    [Fact]
    public void IsTransient_false_for_null()
    {
        PostgresTransientErrors.IsTransient(null).Should().BeFalse();
    }
}

public sealed class PostgresPollyRetryTests
{
    [Fact]
    public async Task ExecuteAsync_retries_until_success()
    {
        var attempts = 0;
        await PostgresPollyRetry.ExecuteAsync(
            async _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new NpgsqlException("Couldn't connect to compute node");
                }

                await Task.Yield();
            },
            NullLogger.Instance,
            CancellationToken.None);

        attempts.Should().Be(3);
    }
}
