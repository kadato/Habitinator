namespace App.Shared.RCL.Services;

public interface IOnboardingStore
{
    Task<bool> IsCompletedAsync(CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(CancellationToken cancellationToken = default);
}
