using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace App.Web.Services;

/// <summary>Detects PostgreSQL / Neon connection failures that are safe to retry.</summary>
public static class PostgresTransientErrors
{
    private static readonly string[] SqlStates = ["57P01", "08006", "08003"];

    private static readonly string[] MessageSubstrings =
    [
        "Connection terminated unexpectedly",
        "terminating connection due to administrator command",
        "Client has encountered a connection error and is not queryable",
        "network issue",
        "early eof",
        "Couldn't connect to compute node",
        "starting up",
    ];

    public static bool IsTransient(Exception? exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is AggregateException agg)
            {
                if (agg.Flatten().InnerExceptions.Any(IsTransient))
                {
                    return true;
                }

                continue;
            }

            if (ex is DbUpdateException dbUpdate && IsTransient(dbUpdate.InnerException))
            {
                return true;
            }

            if (ex is PostgresException pg)
            {
                if (SqlStates.Contains(pg.SqlState, StringComparer.Ordinal))
                {
                    return true;
                }

                if (MessageMatches(pg.Message))
                {
                    return true;
                }
            }

            if (ex is NpgsqlException npg)
            {
                if (npg.IsTransient)
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(npg.SqlState) && SqlStates.Contains(npg.SqlState, StringComparer.Ordinal))
                {
                    return true;
                }

                if (MessageMatches(npg.Message))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool MessageMatches(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return MessageSubstrings.Any(fragment => message.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
