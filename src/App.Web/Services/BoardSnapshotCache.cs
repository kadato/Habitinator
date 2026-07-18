using App.Shared.RCL.Models;

using Microsoft.Extensions.Caching.Memory;

namespace App.Web.Services;

public sealed class BoardSnapshotCache(IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(4);

    public bool TryGet(Guid userId, out BoardSnapshot snapshot) =>
        cache.TryGetValue(userId, out snapshot!);

    public void Set(Guid userId, BoardSnapshot snapshot) =>
        cache.Set(userId, snapshot, Ttl);

    public void Invalidate(Guid userId) => cache.Remove(userId);
}
