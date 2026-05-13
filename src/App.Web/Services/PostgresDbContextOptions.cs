using Microsoft.EntityFrameworkCore;

using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace App.Web.Services;

public static class PostgresDbContextOptions
{
    private static readonly string[] AdditionalTransientSqlStates = ["57P01", "08006", "08003"];

    /// <summary>
    /// Registers Npgsql with EF Core execution-strategy retries for transient failures (Neon restarts, pooler, network).
    /// </summary>
    public static DbContextOptionsBuilder UseNpgsqlWithResilience(this DbContextOptionsBuilder optionsBuilder,
        string connectionString) =>
        optionsBuilder.UseNpgsql(connectionString, ConfigureNpgsqlResilience);

    /// <summary>Same retry policy as <see cref="UseNpgsqlWithResilience" /> for ad-hoc <see cref="DbContext" /> instances.</summary>
    public static void ConfigureNpgsqlResilience(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(16),
            errorCodesToAdd: AdditionalTransientSqlStates);
}
