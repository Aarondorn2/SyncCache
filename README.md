# SyncCache

SyncCache is a .NET library that combines an in-process **memory cache** with **Azure Service Bus topics** so cache entries can be **purged across replicas**. Each instance keeps a local `MemoryCache`; when one instance removes a key, it publishes a small JSON message so other instances drop the same logical entry locally.

Root namespace: **`Noogadev.SyncCache`**.

---

## Concepts

| Piece | Role |
|--------|------|
| **Cache namespace** | Logical partition (`ICachedItem<T>.CacheNamespace`), prefixed into every stored key so different cache types do not collide. |
| **Compound key** | `{CacheNamespace}||{userKey}` in `MemoryCache`. |
| **`PurgeMessage`** | `{ CacheNamespace, Key }`. `Key` null clears all entries whose compound key starts with that namespace prefix (`SynchronizedMemoryCache.RemoveLocal`). |
| **`ISyncCacheTopicProvider`** | **`SubscribeAsync`** — subscription lifecycle (receive path). **`PublishAsync`** — send a purge to the topic. |

---

## Implementation overview

### Typed cache (`SyncCache<K, T>`)

- **`K : ICachedItem<T>, new()`** provides **`CacheNamespace`**, async **`ValueFactory`**, and optional **`MemoryCacheEntryOptions`**.
- **`GetAsync`** uses **`MemoryCache.GetOrCreate`** via **`SynchronizedMemoryCache.GetOrAdd`**.
- **`RemoveAsync`** removes locally, then **`PublishAsync(new PurgeMessage(CacheNamespace, key))`**.

### Local cache (`SynchronizedMemoryCache`)

- Static **`MemoryCache`**; defaults: **30 minute** sliding and **10 hour** absolute expiration (`DefaultCacheEntryOptions`).
- **`RemoveLocal(namespace, key)`** — single key, or **`key == null`** → remove all keys under that namespace prefix (scan `MemoryCache.Keys`).
- **`ClearAll()`** — removes **every** entry (used after a detected subscription outage so stale data is not kept after missed purges).

### Topic pipeline (`ServiceBusSyncCacheTopic`)

**`SubscribeAsync`** is the single entry point for the receiver path (serialized with **`SemaphoreSlim`**):

1. If **`ServiceBusProcessor`** exists and **`IsProcessing`** is true → return (healthy).
2. **`ServiceBusAdministrationClient`** — **`CreateSubscriptionAsync`** if the subscription is missing (409 / entity-already-exists ignored for races).
3. If a processor existed but was not processing → **stop/dispose** it, then **`SynchronizedMemoryCache.ClearAll()`**, then create a **new** processor (reconnect after outage).
4. **`StartProcessingAsync`**.

Message handling: deserialize JSON → **`TopicMessageHandler.ProcessMessage`** → **`CompleteMessageAsync`**. Invalid JSON → dead-letter **`BadPayload`**. Other errors → **abandon** (retry).

Payload: **`application/json`**, camelCase, **`Subject = "purge"`**.

**Disconnects:** the SDK often recovers automatically; when **`IsProcessing`** becomes false, the next **`SubscribeAsync`** run performs reconnect and **full local cache clear**. **`IsProcessing`** can remain true in some failure modes; see limitations below.

### Subscription naming (`SubscriptionNaming`)

Each resolved name is **`sync-cache-{Guid}-{hint}`** (sanitized, length capped), where **`hint`** comes from:

1. **`SubscriptionName`** option if set, else  
2. **`CONTAINER_APP_REPLICA_NAME`**, else **`HOSTNAME`**, else **`Environment.MachineName`**.

That keeps **one subscription per replica** so every instance gets its own copy of topic messages (not competing consumers on a single subscription).

Optional **`SubscriptionAutoDeleteOnIdle`** on **`ServiceBusSyncCacheOptions`** (Azure enforces SKU minimums).

### Periodic recovery

**`SyncCacheSubscriptionRecoveryHostedService`** (**`BackgroundService`**) uses **`PeriodicTimer`** with **`SubscriptionRecoveryCheckInterval`** (default **30 minutes**) and calls **`SubscribeAsync`** on each tick. Healthy processors return immediately.

- Set **`SubscriptionRecoveryCheckInterval = TimeSpan.Zero`** to disable.
- The timer waits the **full interval before the first tick** (startup subscribe still runs via the subscription hosted service).

### Hosted services (DI)

**`AddSyncCacheServiceBusTopic`** registers:

1. **`SyncCacheServiceBusSubscriptionHostedService`** — **`SubscribeAsync`** at host start.  
2. **`SyncCacheSubscriptionRecoveryHostedService`** — periodic **`SubscribeAsync`** as above.

Also registers **`ServiceBusSyncCacheTopic`** singleton and **`ISyncCacheTopicProvider`** → same instance.

### Dependency injection example

```csharp
services.AddSyncCacheServiceBusTopic(o =>
{
    o.ConnectionString = "<connection-string>";
    o.TopicName = "SyncCachePurge";
    o.SubscriptionName = null;
    o.SubscriptionAutoDeleteOnIdle = TimeSpan.FromDays(14);
    o.SubscriptionRecoveryCheckInterval = TimeSpan.FromMinutes(30);
});
```

---

## Azure prerequisites

1. A **Service Bus namespace** with a **topic** matching **`TopicName`**. This library **creates subscriptions at runtime**; it does **not** create the topic.
2. Credentials that allow **subscription management** plus **send** (topic) and **receive** (subscription).

---

## RBAC and credentials

### Connection string (shared access policies)

The policy needs **Manage** (administration client / create subscription) plus **Send** and **Listen** (or an equivalent namespace policy). Avoid **`RootManageSharedAccessKey`** outside dev.

### Azure AD / managed identity

The identity must be able to **create subscriptions** and **send/receive**. **`Azure Service Bus Data Owner`** on the namespace is a common choice. **`Data Sender`** + **`Data Receiver`** are **not** enough if they exclude the manage operations required by **`CreateSubscriptionAsync`**.

- [Managed identities with Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-managed-service-identity)  
- [Azure Service Bus RBAC roles](https://learn.microsoft.com/en-us/azure/service-bus-messaging/authenticate-application)

---

## Operational notes

- **Topic must exist**; subscriptions are provisioned in code for autoscaling replicas.
- Ephemeral subscriptions can accumulate; use **`SubscriptionAutoDeleteOnIdle`** and monitor the namespace.
- **`ServiceBusAdministrationClient`** is constructed during **`SubscribeAsync`** for provisioning; the referenced SDK type does not implement **`IDisposable`** in current versions.

### Limitations

- Recovery is driven by **`IsProcessing == false`** and the periodic timer. Some faults may leave **`IsProcessing`** true while delivery is unhealthy; a future improvement would be wiring **`ProcessErrorAsync`** (or similar) to force reconnect.

---

## Build

```bash
dotnet build src/SyncCache/SyncCache.csproj -c Release
dotnet pack src/SyncCache/SyncCache.csproj -c Release -o artifacts
```

---

## License

See [LICENSE](LICENSE).
