using Npgsql;

namespace App.Web.Services;

/// <summary>PostgreSQL error classification shared by the web services.</summary>
public static class PostgresErrors
{
    public const string UniqueViolationSqlState = "23505";

    public static bool IsUniqueViolation(Exception? exception)
    {
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is PostgresException { SqlState: UniqueViolationSqlState })
            {
                return true;
            }
        }

        return false;
    }
}
