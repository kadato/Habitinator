namespace App.Shared.RCL.Services;

public interface IActivityEventStore
{
    Task AppendAsync(UserActivityEventRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserActivityEventRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserActivityEventRecord>> GetInRangeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    event EventHandler<UserActivityEventRecord>? Appended;
}
