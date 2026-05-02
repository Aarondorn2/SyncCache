using System.Text.Json;
using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Options;
using Noogadev.SyncCache.InternalCache;
using Noogadev.SyncCache.Topic;

namespace Noogadev.SyncCache.ServiceBus;

/// <summary>
/// Azure Service Bus topic implementation of <see cref="ISyncCacheTopicProvider"/> with runtime subscription creation and JSON payloads.
/// </summary>
public sealed class ServiceBusSyncCacheTopic : ISyncCacheTopicProvider, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ServiceBusSyncCacheOptions _options;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private ServiceBusProcessor? _processor;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly string _subscriptionName;

    /// <summary>
    /// Creates a client from <paramref name="options"/>.
    /// </summary>
    public ServiceBusSyncCacheTopic(IOptions<ServiceBusSyncCacheOptions> options)
    {
        _options = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.TopicName);

        _subscriptionName = SubscriptionNaming.Resolve(_options.SubscriptionName);

        _client = new ServiceBusClient(_options.ConnectionString);
        _sender = _client.CreateSender(_options.TopicName);
    }

    /// <inheritdoc />
    public Task PublishAsync(PurgeMessage message, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        var sbMessage = new ServiceBusMessage(body) { ContentType = "application/json", Subject = "purge" };
        return _sender.SendMessageAsync(sbMessage, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_processor is { IsProcessing: true }) return;

            var admin = new ServiceBusAdministrationClient(_options.ConnectionString);
            await EnsureSubscriptionAsync(admin, cancellationToken).ConfigureAwait(false);

            if (_processor != null)
            {
                await StopAndDisposeProcessorAsync(_processor).ConfigureAwait(false);
                _processor = null;
                SynchronizedMemoryCache.ClearAll();
            }

            var processor = CreateProcessor();
            _processor = processor;
            await processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Creates a topic processor for the resolved subscription with manual completion and <see cref="OnProcessMessageAsync"/> wired.
    /// </summary>
    private ServiceBusProcessor CreateProcessor()
    {
        var processor = _client.CreateProcessor(
            _options.TopicName,
            _subscriptionName,
            new ServiceBusProcessorOptions { AutoCompleteMessages = false });

        processor.ProcessMessageAsync += OnProcessMessageAsync;
        return processor;
    }

    /// <summary>
    /// Stops receive processing and releases processor resources; intended for lifecycle transitions and dispose.
    /// </summary>
    /// <param name="processor">Processor to stop and dispose.</param>
    private static async Task StopAndDisposeProcessorAsync(ServiceBusProcessor processor)
    {
        await processor.StopProcessingAsync(CancellationToken.None).ConfigureAwait(false);
        await processor.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the Service Bus subscription exists under the configured topic, applying optional auto-delete-on-idle; ignores conflict when it already exists.
    /// </summary>
    /// <param name="admin">Administration client for the same namespace as <see cref="ServiceBusSyncCacheOptions.ConnectionString"/>.</param>
    /// <param name="cancellationToken">Cancellation for the create operation.</param>
    private async Task EnsureSubscriptionAsync(ServiceBusAdministrationClient admin, CancellationToken cancellationToken)
    {
        var createOptions = new CreateSubscriptionOptions(_options.TopicName, _subscriptionName);
        if (_options.SubscriptionAutoDeleteOnIdle != null)
            createOptions.AutoDeleteOnIdle = _options.SubscriptionAutoDeleteOnIdle.Value;

        try
        {
            await admin.CreateSubscriptionAsync(createOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
        }
    }

    /// <summary>
    /// Deserializes a purge payload, applies it locally via <see cref="TopicMessageHandler.ProcessMessage"/>, completes on success, dead-letters malformed JSON, or abandons on other failures.
    /// </summary>
    /// <param name="args">Service Bus message processing context.</param>
    private static async Task OnProcessMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var purge = Deserialize(args.Message);
            TopicMessageHandler.ProcessMessage(purge);
            await args.CompleteMessageAsync(args.Message).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            await args.DeadLetterMessageAsync(args.Message, deadLetterReason: "BadPayload").ConfigureAwait(false);
        }
        catch (Exception)
        {
            await args.AbandonMessageAsync(args.Message).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Parses a <see cref="PurgeMessage"/> from the message body using the shared JSON options.
    /// </summary>
    /// <param name="message">Received Service Bus message.</param>
    /// <returns>Non-null purge payload.</returns>
    /// <exception cref="JsonException">Thrown when deserialization yields null.</exception>
    private static PurgeMessage Deserialize(ServiceBusReceivedMessage message) =>
        JsonSerializer.Deserialize<PurgeMessage>(message.Body.ToMemory().Span, Json)
        ?? throw new JsonException("Empty purge payload.");

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_processor != null)
            {
                await StopAndDisposeProcessorAsync(_processor).ConfigureAwait(false);
                _processor = null;
            }

            await _sender.DisposeAsync().ConfigureAwait(false);
            await _client.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
            _lifecycle.Dispose();
        }
    }
}
