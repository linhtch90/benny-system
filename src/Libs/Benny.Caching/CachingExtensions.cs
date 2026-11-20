using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Benny.Caching;

public static class CachingExtensions
{
    public static IServiceCollection AddBennyCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 50_000;
        });

        var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));

        services.AddSingleton<ITwoTierNearCache, TwoTierNearCache>();
        services.AddSingleton<IIdempotencyService, RedisIdempotencyService>();
        services.AddHostedService<CacheInvalidationSubscriber>();

        return services;
    }
}
