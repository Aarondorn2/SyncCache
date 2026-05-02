using Microsoft.Extensions.Caching.Memory;

namespace Noogadev.SyncCache.InternalCache;

internal static class SynchronizedMemoryCache
{
    public static readonly MemoryCacheEntryOptions DefaultCacheEntryOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(30),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(10),
    };

    private static readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());

    internal static Task<T> GetOrAdd<T>(
        string cacheNamespace,
        string key,
        Func<string, Task<T>> valueFactory,
        MemoryCacheEntryOptions? options = null
    ) {
        var compoundKey = CreateCompoundKey(cacheNamespace, key);
        options ??= DefaultCacheEntryOptions;

        return _memoryCache.GetOrCreate(compoundKey, async entry =>
        {
            ApplyEntryOptions(entry, options);
            return await valueFactory(key);
        })!;
    }

    internal static void ClearAll()
    {
        foreach (var key in _memoryCache.Keys)
        {
            _memoryCache.Remove(key);
        }
    }

    internal static void RemoveLocal(string cacheNamespace, string? key)
    {
        if (key != null)
        {
            _memoryCache.Remove(CreateCompoundKey(cacheNamespace, key));
            return;
        }

        if (_memoryCache is MemoryCache mc)
        {
            foreach (var cachedKey in mc.Keys)
            {
                var stringCachedKey = cachedKey?.ToString();
                if (stringCachedKey?.StartsWith(cacheNamespace + KeyDelimiter, StringComparison.Ordinal) == true)
                    _memoryCache.Remove(stringCachedKey);
            }
        }
    }

    private const string KeyDelimiter = "||";

    /// <summary>
    /// Builds the in-memory cache key as <c>{namespace}{delimiter}{key}</c>.
    /// </summary>
    /// <param name="cacheNamespace">Logical cache namespace; must be non-whitespace.</param>
    /// <param name="key">User key; must be non-whitespace.</param>
    /// <returns>Compound key stored in <see cref="MemoryCache"/>.</returns>
    private static string CreateCompoundKey(string cacheNamespace, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return cacheNamespace + KeyDelimiter + key;
    }

    /// <summary>
    /// Copies expiration, priority, size, tokens, and post-eviction callbacks from <paramref name="options"/> onto <paramref name="entry"/>.
    /// </summary>
    /// <param name="entry">Cache entry being configured.</param>
    /// <param name="options">Source options from the cached-item definition or defaults.</param>
    private static void ApplyEntryOptions(ICacheEntry entry, MemoryCacheEntryOptions options)
    {
        entry.SlidingExpiration = options.SlidingExpiration;
        entry.AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow;
        entry.AbsoluteExpiration = options.AbsoluteExpiration;
        entry.Priority = options.Priority;
        entry.Size = options.Size;

        foreach (var token in options.ExpirationTokens)
        {
            entry.AddExpirationToken(token);
        }

        foreach (var registration in options.PostEvictionCallbacks.Where(x => x.EvictionCallback != null))
        {
            entry.RegisterPostEvictionCallback(registration.EvictionCallback!, registration.State);
        }
    }
}
