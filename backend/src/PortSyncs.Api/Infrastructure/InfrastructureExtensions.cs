using PortSyncs.Api.Infrastructure.Maps;
using PortSyncs.Api.Infrastructure.Messaging;
using PortSyncs.Api.Infrastructure.Storage;

namespace PortSyncs.Api.Infrastructure;

public static class InfrastructureExtensions
{
    /// <summary>
    /// Wires the portable infrastructure (T75). Each service has a simple H1 implementation;
    /// cloud implementations are added later and chosen by configuration, not code changes.
    /// </summary>
    public static IServiceCollection AddPortSyncsInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var storage = config.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
        services.AddSingleton(storage);
        services.AddSingleton<IFileStorage>(storage.Provider switch
        {
            "Local" => new LocalDiskFileStorage(storage),
            _ => throw new InvalidOperationException($"Unknown Storage__Provider '{storage.Provider}'."),
        });

        var messaging = config["Messaging:Provider"] ?? "InProcess";
        if (messaging != "InProcess")
            throw new InvalidOperationException($"Unknown Messaging__Provider '{messaging}'. RabbitMQ comes next.");
        services.AddSingleton<IEventBus, InProcessEventBus>();

        // Cache: in-memory now; Redis via AddStackExchangeRedisCache when Cache__Provider=Redis.
        services.AddDistributedMemoryCache();

        services.AddSingleton<IMapProvider, DeepLinkMapProvider>();
        return services;
    }
}
