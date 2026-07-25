using System.Collections.Concurrent;

using App.Shared.RCL.Services;

namespace App.Web.Services;

public sealed class ActivityStatisticsCache
{
    private readonly ConcurrentDictionary<Guid, UserCache> _userCaches = new();

    public UserCache GetOrCreate(Guid userId)
    {
        return _userCaches.GetOrAdd(userId, _ => new UserCache());
    }

    public void Invalidate(Guid userId)
    {
        _userCaches.TryRemove(userId, out _);
    }

#pragma warning disable CA1034 // Nested type visible - internal implementation detail exposed for internal use
    public sealed class UserCache
#pragma warning restore CA1034
    {
        public ConcurrentDictionary<(string? Period, string? Tag, DateOnly Today), ActivityDashboardDto> Dashboard { get; } = new();
        public ConcurrentDictionary<(string? Period, string? Tag, DateOnly Today), DailyContributionsViewDto> DailyContributions { get; } = new();
        public ConcurrentDictionary<(DateOnly Day, string? Tag), ActivityDayDetailDto> DayDetail { get; } = new();
    }
}
