using System.Collections.Concurrent;

using App.Shared.RCL.Models;

namespace App.Web.Services;

public sealed class BoardSnapshotCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(4);
    private readonly ConcurrentDictionary<Guid, CacheEntry> _entries = new();

    public bool TryGet(Guid userId, out BoardSnapshot snapshot)
    {
        snapshot = default!;
        if (!_entries.TryGetValue(userId, out var entry))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow - entry.StoredAtUtc > Ttl)
        {
            _entries.TryRemove(userId, out _);
            return false;
        }

        snapshot = entry.Snapshot;
        return true;
    }

    public void Set(Guid userId, BoardSnapshot snapshot) =>
        _entries[userId] = new CacheEntry(snapshot, DateTimeOffset.UtcNow);

    public void Invalidate(Guid userId) => _entries.TryRemove(userId, out _);

    private sealed record CacheEntry(BoardSnapshot Snapshot, DateTimeOffset StoredAtUtc);
}
