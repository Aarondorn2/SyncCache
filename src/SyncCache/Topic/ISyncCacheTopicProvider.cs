namespace Noogadev.SyncCache.Topic;

/// <summary>
/// Abstraction for subscribing to purge notifications and publishing purge messages.
/// </summary>
public interface ISyncCacheTopicProvider
{
    /// <summary>
    /// Ensures the transport subscription is active and begins receiving purge messages.
    /// 
    /// This method should be idopetent as it will be periodically called by <see cref="TopicRecoveryService"/>.
    /// This ensures dropped connections get reconnected. If a drop is detected, this method should clear all cached values.
    /// </summary>
    Task SubscribeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a purge payload to all subscribers.
    /// </summary>
    Task PublishAsync(PurgeMessage message, CancellationToken cancellationToken = default);
}
