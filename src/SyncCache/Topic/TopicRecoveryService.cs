using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Noogadev.SyncCache.ServiceBus;

namespace Noogadev.SyncCache.Topic;

/// <summary>
/// Ensures subscriptions stay connected by periodically re-invoking the subscribe method
/// </summary>
/// <param name="topic"></param>
/// <param name="options"></param>
internal sealed class TopicRecoveryService(
    ISyncCacheTopicProvider topic,
    IOptions<ServiceBusSyncCacheOptions> options
) : BackgroundService
{
    private readonly ISyncCacheTopicProvider _topic = topic;
    private readonly IOptions<ServiceBusSyncCacheOptions> _options = options;

    /// <summary>
    /// Ensures an initial subscription, then periodically re-invokes <see cref="ISyncCacheTopicProvider.SubscribeAsync(System.Threading.CancellationToken)"/> when a positive recovery interval is configured.
    /// </summary>
    /// <param name="cancellationToken">Triggered when the host stops.</param>
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await _topic.SubscribeAsync(cancellationToken).ConfigureAwait(false);

        var interval = _options.Value.SubscriptionRecoveryCheckInterval;
        if (interval <= TimeSpan.Zero) return;

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await _topic.SubscribeAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
