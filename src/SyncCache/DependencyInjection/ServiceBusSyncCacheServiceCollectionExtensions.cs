using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Noogadev.SyncCache.ServiceBus;
using Noogadev.SyncCache.Topic;

namespace Noogadev.SyncCache.DependencyInjection;

/// <summary>
/// Registers Azure Service Bus–backed sync cache infrastructure and discovers <see cref="ICachedItem{T}"/> implementations.
/// </summary>
public static class ServiceBusSyncCacheServiceCollectionExtensions
{
    /// <summary>
    /// Adds Service Bus topic client, hosted recovery polling, and scoped <see cref="SyncCache{K,T}"/> registrations for concrete <see cref="ICachedItem{T}"/> types found in scanned assemblies.
    /// </summary>
    /// <param name="services">Application services.</param>
    /// <param name="configure">Binds <see cref="ServiceBusSyncCacheOptions"/>.</param>
    /// <param name="cachedItemAssemblies">
    /// Assemblies to scan for non-abstract classes implementing <see cref="ICachedItem{T}"/> (constructors are satisfied by the service collection).
    /// When empty, <see cref="Assembly.GetEntryAssembly"/> is used when non-null.
    /// </param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddSyncCacheServiceBusTopic(
        this IServiceCollection services,
        Action<ServiceBusSyncCacheOptions> configure,
        params Assembly[] cachedItemAssemblies)
    {
        services.Configure(configure);

        services.TryAddSingleton<ServiceBusSyncCacheTopic>();
        services.TryAddSingleton<ISyncCacheTopicProvider>(sp => sp.GetRequiredService<ServiceBusSyncCacheTopic>());
        services.AddHostedService<TopicRecoveryService>();

        RegisterSyncCachesFromCachedItems(services, cachedItemAssemblies);

        return services;
    }

    /// <summary>
    /// Scans <paramref name="assemblies"/> for concrete <see cref="ICachedItem{T}"/> types and registers a scoped <see cref="SyncCache{K,T}"/> for each discovered pair.
    /// </summary>
    /// <param name="services">Application services.</param>
    /// <param name="assemblies">Assemblies to scan; when empty, the entry assembly is used when present.</param>
    private static void RegisterSyncCachesFromCachedItems(IServiceCollection services, Assembly[] assemblies)
    {
        IEnumerable<Assembly> toScan = assemblies.Length > 0
            ? assemblies
            : EnumerateEntryAssembly();

        foreach (var assembly in toScan)
        {
            if (assembly == null)
                continue;

            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type is not { IsClass: true, IsAbstract: false })
                    continue;

                foreach (var iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != typeof(ICachedItem<>))
                        continue;

                    var valueType = iface.GetGenericArguments()[0];
                    var syncCacheType = typeof(SyncCache<,>).MakeGenericType(type, valueType);
                    services.TryAdd(ServiceDescriptor.Scoped(type, type));
                    services.TryAdd(ServiceDescriptor.Scoped(syncCacheType, syncCacheType));
                }
            }
        }
    }

    /// <summary>
    /// Yields <see cref="Assembly.GetEntryAssembly"/> when it is non-null, for default <see cref="ICachedItem{T}"/> discovery.
    /// </summary>
    private static IEnumerable<Assembly> EnumerateEntryAssembly()
    {
        var entry = Assembly.GetEntryAssembly();
        if (entry != null)
            yield return entry;
    }

    /// <summary>
    /// Returns all types from <paramref name="assembly"/>; on <see cref="ReflectionTypeLoadException"/>, returns successfully loaded types only.
    /// </summary>
    /// <param name="assembly">Assembly whose types are enumerated.</param>
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).Cast<Type>();
        }
    }
}
