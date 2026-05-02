using Microsoft.Extensions.Caching.Memory;

namespace Noogadev.SyncCache;

/// <summary>
/// Describes how a typed cache entry is loaded and optionally expired for a given logical namespace.
/// </summary>
/// <typeparam name="T">Type of values stored for each key.</typeparam>
public interface ICachedItem<T>
{
    /// <summary>
    /// Prefix segment used to build compound keys in the synchronized memory cache.
    /// </summary>
    string CacheNamespace { get; }

    /// <summary>
    /// Loads a value when the key is not already cached.
    /// </summary>
    Func<string, Task<T>> ValueFactory { get; }

    /// <summary>
    /// Optional per-entry memory cache options; when null, library default expiration options apply.
    /// </summary>
    MemoryCacheEntryOptions? Options { get; }
}
