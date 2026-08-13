using Npgsql;

namespace App.Web.Services;

/// <summary>
/// Ensures connection parameters tolerate managed Postgres cold starts, e.g. Neon scale-to-zero.
/// </summary>
public static class PostgresResilienceConnectionString
{
    /// <summary>Minimum <see cref="NpgsqlConnectionStringBuilder.Timeout"/> in seconds for opening a connection.</summary>
    public const int MinimumConnectionTimeoutSeconds = 15;

    /// <summary>
    /// Raises <see cref="NpgsqlConnectionStringBuilder.Timeout"/> when it is below <see cref="MinimumConnectionTimeoutSeconds"/>.
    /// </summary>
    public static string EnsureColdStartTimeouts(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        if (b.Timeout < MinimumConnectionTimeoutSeconds)
        {
            b.Timeout = MinimumConnectionTimeoutSeconds;
        }

        return b.ConnectionString;
    }
}
