using System;
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

    public sealed class UserCache
    {
        public ConcurrentDictionary<(string? Period, string? Tag, DateOnly Today), ActivityDashboardDto> Dashboard { get; } = new();
        public ConcurrentDictionary<(string? Period, string? Tag, DateOnly Today), DailyContributionsViewDto> DailyContributions { get; } = new();
        public ConcurrentDictionary<(DateOnly Day, string? Tag), ActivityDayDetailDto> DayDetail { get; } = new();
    }
}
