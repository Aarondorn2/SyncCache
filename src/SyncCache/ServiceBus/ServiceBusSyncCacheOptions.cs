namespace Noogadev.SyncCache.ServiceBus;

/// <summary>
/// Configuration for <see cref="ServiceBusSyncCacheTopic"/>.
/// </summary>
public sealed class ServiceBusSyncCacheOptions
{
    /// <summary>
    /// Service Bus namespace connection string (or compatible credential wiring via client overrides elsewhere).
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// Topic used for purge broadcasts.
    /// </summary>
    public string TopicName { get; init; } = "SyncCachePurge";

    /// <summary>
    /// Subscription name under <see cref="TopicName"/>; when null, a unique name is derived for the runtime (see subscription naming logic).
    /// </summary>
    public string? SubscriptionName { get; init; }

    /// <summary>
    /// When set, passed to subscription creation as idle auto-delete (SKU minimums apply).
    /// </summary>
    public TimeSpan? SubscriptionAutoDeleteOnIdle { get; init; }

    /// <summary>
    /// Interval for periodic topic subscription recovery calls. Use <c>TimeSpan.Zero</c> to disable.
    /// </summary>
    public TimeSpan SubscriptionRecoveryCheckInterval { get; init; } = TimeSpan.FromMinutes(30);
}
