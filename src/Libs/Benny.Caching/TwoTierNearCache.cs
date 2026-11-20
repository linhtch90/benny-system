using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Benny.Caching;

public interface ITwoTierNearCache
{
    ValueTask<T?> GetOrLoadAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> loader,
        TimeSpan l1Ttl,
        TimeSpan l2Ttl,
        CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan l1Ttl, TimeSpan l2Ttl, CancellationToken cancellationToken = default);

    Task InvalidateAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class TwoTierNearCache(
    IMemoryCache l1Cache,
    IConnectionMultiplexer redis,
    ILogger<TwoTierNearCache> logger) : ITwoTierNearCache
{
    public const string InvalidationChannel = "benny:cache:invalidation";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inFlight = new();

    public async ValueTask<T?> GetOrLoadAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> loader,
        TimeSpan l1Ttl,
        TimeSpan l2Ttl,
        CancellationToken cancellationToken = default)
    {
        if (l1Cache.TryGetValue(key, out T? l1Value))
        {
            return l1Value;
        }

        var db = redis.GetDatabase();
        var l2Bytes = await db.StringGetAsync(key);
        if (l2Bytes.HasValue)
        {
            var deserialized = JsonSerializer.Deserialize<T>(l2Bytes.ToString(), JsonOptions);
            if (deserialized is not null)
            {
                l1Cache.Set(key, deserialized, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = l1Ttl,
                    Size = 1
                });
                return deserialized;
            }
        }

        // Single-flight coalescing to prevent cache stampede on L2 miss
        var lazyTask = _inFlight.GetOrAdd(key, _ => new Lazy<Task<object?>>(async () =>
        {
            var loaded = await loader(cancellationToken);
            if (loaded is not null)
            {
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                var serialized = JsonSerializer.Serialize(loaded, JsonOptions);
                await db.StringSetAsync(key, serialized, l2Ttl + jitter);
                l1Cache.Set(key, loaded, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = l1Ttl,
                    Size = 1
                });
            }
            return loaded;
        }, LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var result = await lazyTask.Value;
            return result is T typed ? typed : default;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan l1Ttl, TimeSpan l2Ttl, CancellationToken cancellationToken = default)
    {
        var db = redis.GetDatabase();
        var serialized = JsonSerializer.Serialize(value, JsonOptions);
        await db.StringSetAsync(key, serialized, l2Ttl);

        l1Cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = l1Ttl,
            Size = 1
        });

        await redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(InvalidationChannel),
            key);
        logger.LogDebug("Set and broadcast cache key {CacheKey}", key);
    }

    public async Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
    {
        l1Cache.Remove(key);
        var db = redis.GetDatabase();
        await db.KeyDeleteAsync(key);
        await redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(InvalidationChannel),
            key);
    }
}
