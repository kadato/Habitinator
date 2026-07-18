using System.Buffers;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace App.Web.Services;

/// <summary>Detects PostgreSQL / Neon connection failures that are safe to retry.</summary>
public static class PostgresTransientErrors
{
    private static readonly SearchValues<string> SqlStates = SearchValues.Create(
        ["57P01", "08006", "08003"], StringComparison.Ordinal);

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
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (IsSingleExceptionTransient(ex, out bool stopCheckingChain))
            {
                return true;
            }

            if (stopCheckingChain)
            {
                break;
            }
        }

        return false;
    }

    private static bool IsSingleExceptionTransient(Exception ex, out bool stopCheckingChain)
    {
        stopCheckingChain = false;

        if (ex is AggregateException agg)
        {
            stopCheckingChain = true;
            return agg.Flatten().InnerExceptions.Any(IsTransient);
        }

        if (ex is DbUpdateException dbUpdate)
        {
            return IsTransient(dbUpdate.InnerException);
        }

        if (ex is PostgresException pg)
        {
            return SqlStates.Contains(pg.SqlState) || MessageMatches(pg.Message);
        }

        if (ex is NpgsqlException npg)
        {
            return npg.IsTransient
                || (!string.IsNullOrEmpty(npg.SqlState) && SqlStates.Contains(npg.SqlState))
                || MessageMatches(npg.Message);
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
