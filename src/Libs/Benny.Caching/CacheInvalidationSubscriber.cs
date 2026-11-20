using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Benny.Caching;

public sealed class CacheInvalidationSubscriber(
    IConnectionMultiplexer redis,
    IMemoryCache l1Cache,
    ILogger<CacheInvalidationSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(TwoTierNearCache.InvalidationChannel),
            (_, message) =>
            {
                if (message.HasValue)
                {
                    var key = message.ToString();
                    l1Cache.Remove(key);
                    logger.LogDebug("Evicted L1 cache key {CacheKey} via Redis Pub/Sub", key);
                }
            });

        logger.LogInformation("Subscribed to Redis cache invalidation channel {Channel}", TwoTierNearCache.InvalidationChannel);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
