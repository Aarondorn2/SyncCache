using Noogadev.SyncCache.InternalCache;
using Noogadev.SyncCache.Topic;

namespace Noogadev.SyncCache;

/// <summary>
/// Per-<typeparamref name="K"/> typed facade over the process memory cache and topic purge propagation.
/// </summary>
/// <typeparam name="K">Cached-item definition type; resolved from DI so <typeparamref name="K"/> may use constructor injection.</typeparam>
/// <typeparam name="T">Cached value type.</typeparam>
public sealed class SyncCache<K, T> where K : ICachedItem<T>
{
    private readonly K _item;
    private readonly ISyncCacheTopicProvider _topic;

    /// <summary>
    /// Creates a cache that uses <paramref name="item"/> for namespace and value factory and <paramref name="topic"/> for cross-instance invalidation.
    /// </summary>
    public SyncCache(K item, ISyncCacheTopicProvider topic)
    {
        _item = item;
        _topic = topic;
    }

    /// <summary>
    /// Gets or materializes the value for <paramref name="key"/> using <typeparamref name="K"/>.
    /// </summary>
    public Task<T> GetAsync(string key) =>
        SynchronizedMemoryCache.GetOrAdd(_item.CacheNamespace, key, _item.ValueFactory, _item.Options);

    /// <summary>
    /// Removes the entry locally and publishes a purge so peers remove the same key (or clears by namespace when <paramref name="key"/> is null).
    /// </summary>
    public async Task RemoveAsync(string? key, CancellationToken cancellationToken = default)
    {
        SynchronizedMemoryCache.RemoveLocal(_item.CacheNamespace, key);
        await _topic.PublishAsync(new PurgeMessage(_item.CacheNamespace, key), cancellationToken).ConfigureAwait(false);
    }
}
