using App.Shared.RCL.Services;

namespace App.Web.Services;

public sealed class ActivityStatisticsCache
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, UserCache> _userCaches = new();

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
        private const int MaxEntriesPerUser = 64;

        public BoundedCache<(string? Period, string? Tag, DateOnly Today), ActivityDashboardDto> Dashboard { get; } = new(MaxEntriesPerUser);

        public BoundedCache<(string? Period, string? Tag, DateOnly Today), DailyContributionsViewDto> DailyContributions { get; } = new(MaxEntriesPerUser);

        public BoundedCache<(string? Period, string? Tag, DateOnly Today), HabitContributionsViewDto> HabitContributions { get; } = new(MaxEntriesPerUser);

        public BoundedCache<(DateOnly Day, string? Tag), ActivityDayDetailDto> DayDetail { get; } = new(MaxEntriesPerUser);
    }

    /// <summary>Thread-safe dictionary with a fixed capacity; evicts the oldest entries when full.</summary>
    public sealed class BoundedCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, TValue> _items = [];
        private readonly Queue<TKey> _insertionOrder = new();

        public BoundedCache(int capacity)
        {
            _capacity = Math.Max(1, capacity);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (_items)
            {
                return _items.TryGetValue(key, out value!);
            }
        }

        public void Set(TKey key, TValue value)
        {
            lock (_items)
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
