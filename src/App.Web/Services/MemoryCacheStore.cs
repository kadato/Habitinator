using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Caching.Memory;

namespace App.Web.Services;

public sealed class MemoryCacheStore<TValue>(IMemoryCache cache, string keyPrefix, TimeSpan ttl)
    where TValue : class
{
    public bool TryGet(Guid key, [MaybeNullWhen(false)] out TValue value)
    {
        if (cache.TryGetValue(CacheKey(key), out var found) && found is TValue typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    public void Set(Guid key, TValue value) => cache.Set(CacheKey(key), value, ttl);

    public void Invalidate(Guid key) => cache.Remove(CacheKey(key));

    private string CacheKey(Guid key) => keyPrefix + key;
}
