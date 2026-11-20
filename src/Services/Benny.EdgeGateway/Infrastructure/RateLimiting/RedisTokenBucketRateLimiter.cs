using StackExchange.Redis;

namespace Benny.EdgeGateway.Infrastructure.RateLimiting;

public sealed record RateLimitDecision(bool Allowed, long RemainingTokens, int RetryAfterSeconds);

public interface ITokenBucketRateLimiter
{
    Task<RateLimitDecision> TryConsumeAsync(string tenantId, int capacity = 200, double refillPerSecond = 100.0, CancellationToken cancellationToken = default);
}

public sealed class RedisTokenBucketRateLimiter(IConnectionMultiplexer redis) : ITokenBucketRateLimiter
{
    private const string TokenBucketLua = """
        local key = KEYS[1]
        local capacity = tonumber(ARGV[1])
        local refill_rate = tonumber(ARGV[2])
        local now_ms = tonumber(ARGV[3])

        local data = redis.call('HMGET', key, 'tokens', 'last_ms')
        local tokens = tonumber(data[1])
        local last_ms = tonumber(data[2])

        if not tokens then
            tokens = capacity
            last_ms = now_ms
        else
            local elapsed_sec = math.max(0, (now_ms - last_ms) / 1000.0)
            tokens = math.min(capacity, tokens + (elapsed_sec * refill_rate))
            last_ms = now_ms
        end

        if tokens >= 1 then
            tokens = tokens - 1
            redis.call('HMSET', key, 'tokens', tokens, 'last_ms', last_ms)
            redis.call('PEXPIRE', key, 60000)
            return {1, math.floor(tokens)}
        else
            redis.call('HMSET', key, 'tokens', tokens, 'last_ms', last_ms)
            redis.call('PEXPIRE', key, 60000)
            return {0, 0}
        end
        """;

    public async Task<RateLimitDecision> TryConsumeAsync(
        string tenantId,
        int capacity = 200,
        double refillPerSecond = 100.0,
        CancellationToken cancellationToken = default)
    {
        var db = redis.GetDatabase();
        var key = $"benny:ratelimit:{tenantId}";
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
            TokenBucketLua,
            [key],
            [capacity, refillPerSecond, nowMs]);

        if (result is null || result.Length < 2)
        {
            return new RateLimitDecision(true, capacity, 0);
        }

        var allowed = (int)result[0] == 1;
        var remaining = (long)result[1];
        return new RateLimitDecision(allowed, remaining, allowed ? 0 : 1);
    }
}
