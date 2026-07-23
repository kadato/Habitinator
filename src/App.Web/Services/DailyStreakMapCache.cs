using Microsoft.Extensions.Caching.Memory;

namespace App.Web.Services;

public sealed class DailyStreakMapCache(IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    public bool TryGet(Guid userId, out Dictionary<Guid, int> streaks) =>
        cache.TryGetValue(StreakKey(userId), out streaks!);

    public void Set(Guid userId, Dictionary<Guid, int> streaks) =>
        cache.Set(StreakKey(userId), streaks, Ttl);

    public void Invalidate(Guid userId) => cache.Remove(StreakKey(userId));

    private static string StreakKey(Guid userId) => $"streak_map_{userId}";
}
