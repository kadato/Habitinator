using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace App.Web.Services;

public static class PostgresDbContextOptions
{
    /// <summary>Registers Npgsql with EF Core execution-strategy retries for transient failures. Neon restarts, pooler, and network issues.</summary>
    public static void ConfigureNpgsqlResilience(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.EnableRetryOnFailure(
            maxRetryCount: PostgresRetryDefaults.RetryMaxCount,
            maxRetryDelay: PostgresRetryDefaults.RetryMaxDelay,
            errorCodesToAdd: PostgresTransientErrors.AdditionalSqlStates);
}
