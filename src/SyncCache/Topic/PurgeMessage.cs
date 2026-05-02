namespace Noogadev.SyncCache.Topic;

/// <summary>
/// Message payload broadcast when cache entries should be purged on remote instances.
/// </summary>
/// <param name="CacheNamespace">Namespace segment matching the publisher's cache namespace.</param>
/// <param name="Key">Specific user key to remove, or null to remove all keys under <paramref name="CacheNamespace"/>.</param>
public sealed record PurgeMessage(string CacheNamespace, string? Key);
