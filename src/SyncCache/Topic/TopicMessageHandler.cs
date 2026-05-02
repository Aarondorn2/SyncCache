using Noogadev.SyncCache.InternalCache;

namespace Noogadev.SyncCache.Topic;

/// <summary>
/// Applies incoming purge payloads to the local memory cache.
/// </summary>
public static class TopicMessageHandler
{
    /// <summary>
    /// Removes entries described by <paramref name="message"/> from local cache only (no republish).
    /// </summary>
    public static void ProcessMessage(PurgeMessage message)
        => SynchronizedMemoryCache.RemoveLocal(message.CacheNamespace, message.Key);
}
