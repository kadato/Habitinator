using Npgsql;

namespace App.Web.Services;

/// <summary>
/// Ensures connection parameters tolerate managed Postgres cold starts, e.g. Neon scale-to-zero.
/// </summary>
public static class PostgresResilienceConnectionString
{
    /// <summary>Minimum <see cref="NpgsqlConnectionStringBuilder.Timeout"/> in seconds for opening a connection.</summary>
    public const int MinimumConnectionTimeoutSeconds = 15;

    /// <summary>Cap for <see cref="NpgsqlConnectionStringBuilder.MaxPoolSize"/> so idle demo traffic holds few server slots.</summary>
    public const int MaxPoolSizeCap = 5;

    /// <summary>Target <see cref="NpgsqlConnectionStringBuilder.ConnectionIdleLifetime"/> in seconds, well under Neon 5 minute suspend.</summary>
    public const int ConnectionIdleLifetimeSeconds = 30;

    /// <summary>Target <see cref="NpgsqlConnectionStringBuilder.ConnectionPruningInterval"/> in seconds.</summary>
    public const int ConnectionPruningIntervalSeconds = 5;

    /// <summary>
    /// Raises <see cref="NpgsqlConnectionStringBuilder.Timeout"/> when it is below <see cref="MinimumConnectionTimeoutSeconds"/>.
    /// Also caps pool size and shortens idle lifetime so Neon sees no activity and can scale to zero.
    /// </summary>
    public static string EnsureColdStartTimeouts(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        if (b.Timeout < MinimumConnectionTimeoutSeconds)
        {
            b.Timeout = MinimumConnectionTimeoutSeconds;
        }

        if (b.MaxPoolSize > MaxPoolSizeCap)
        {
            b.MaxPoolSize = MaxPoolSizeCap;
        }

        if (b.MinPoolSize > 0)
        {
            b.MinPoolSize = 0;
        }

        if (b.ConnectionIdleLifetime > ConnectionIdleLifetimeSeconds)
        {
            b.ConnectionIdleLifetime = ConnectionIdleLifetimeSeconds;
        }

        if (b.ConnectionPruningInterval > ConnectionPruningIntervalSeconds)
        {
            b.ConnectionPruningInterval = ConnectionPruningIntervalSeconds;
        }

        b.KeepAlive = 0;

        return b.ConnectionString;
    }
}
