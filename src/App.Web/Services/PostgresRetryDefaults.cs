namespace App.Web.Services;

public static class PostgresRetryDefaults
{
    public const int RetryMaxCount = 5;

    public static readonly TimeSpan RetryMaxDelay = TimeSpan.FromSeconds(16);
}
