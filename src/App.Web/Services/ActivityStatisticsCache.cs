using System.Diagnostics.CodeAnalysis;

using App.Shared.RCL.Services;

using Microsoft.Extensions.Caching.Memory;

namespace App.Web.Services;

/// <summary>
///     Per-user statistics caches. The cache holds at most 200 users and expires
///     idle users, so memory stays bounded on a small plan.
/// </summary>
public sealed class ActivityStatisticsCache : IDisposable
{
    private const int MaxUsers = 200;
    private static readonly TimeSpan IdleExpiration = TimeSpan.FromMinutes(30);

    private readonly MemoryCache _userCaches = new(new MemoryCacheOptions
    {
        SizeLimit = MaxUsers,
        CompactionPercentage = 0.25
    });

    public UserCache GetOrCreate(Guid userId)
    {
        return _userCaches.GetOrCreate(userId, static entry =>
        {
            entry.Size = 1;
            entry.SlidingExpiration = IdleExpiration;
            return new UserCache();
        })!;
    }

    public void Invalidate(Guid userId)
    {
        _userCaches.Remove(userId);
    }

    public void Dispose()
    {
        _userCaches.Dispose();
    }

#pragma warning disable CA1034 // Nested type visible - internal implementation detail exposed for internal use
    public sealed class UserCache
#pragma warning restore CA1034
    {
        private const int MaxEntriesPerUser = 32;

        public BoundedCache<(string? Period, string? Tag, DateOnly Today), ActivityOverviewDto> Overview { get; } = new(MaxEntriesPerUser);

        public BoundedCache<(string? Period, string? Tag, DateOnly Today), ActivityDashboardDto> Dashboard { get; } = new(MaxEntriesPerUser);

        public BoundedCache<(string? Period, string? Tag, DateOnly Today), DailyContributionsViewDto> DailyContributions { get; } = new(MaxEntriesPerUser);

        public BoundedCache<(string? Period, string? Tag, DateOnly Today), HabitContributionsViewDto> HabitContributions { get; } = new(MaxEntriesPerUser);

        public BoundedCache<(DateOnly Day, string? Tag), ActivityDayDetailDto> DayDetail { get; } = new(MaxEntriesPerUser);
    }

    /// <summary>Thread-safe dictionary with a fixed capacity. Evicts the oldest entries when full.</summary>
    public sealed class BoundedCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, TValue> _items = [];
        private readonly Queue<TKey> _insertionOrder = new();
        private readonly System.Threading.Lock _lock = new();

        public BoundedCache(int capacity)
        {
            _capacity = Math.Max(1, capacity);
        }

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            lock (_lock)
            {
                return _items.TryGetValue(key, out value);
            }
        }

        public void Set(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_items.ContainsKey(key))
                {
                    _items[key] = value;
                    return;
                }

                _items[key] = value;
                _insertionOrder.Enqueue(key);
                while (_insertionOrder.Count > _capacity)
                {
                    _items.Remove(_insertionOrder.Dequeue());
                }
            }
        }
    }
}
