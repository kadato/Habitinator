namespace App.Shared.RCL.Services;

public sealed class SystemClock : IClock
{
    /// <summary>Shared instance for static contexts that need a clock.</summary>
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
