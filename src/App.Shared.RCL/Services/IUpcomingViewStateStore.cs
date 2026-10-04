namespace App.Shared.RCL.Services;

public sealed record UpcomingViewState(
    string? View,
    string? Filter);

public interface IUpcomingViewStateStore
{
    Task<UpcomingViewState?> GetAsync(CancellationToken cancellationToken = default);

    Task SetAsync(UpcomingViewState state, CancellationToken cancellationToken = default);
}
