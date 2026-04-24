namespace App.Shared.RCL.Services;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
