using System.Text.Json;
using StackExchange.Redis;

namespace Benny.Caching;

public sealed record IdempotencyAcquireResult(IdempotencyState State, string? CachedPayload);

public enum IdempotencyState
{
    Acquired,
    InProgress,
    Completed
}

public interface IIdempotencyService
{
    Task<IdempotencyAcquireResult> TryAcquireAsync(string tenantId, string idempotencyKey, TimeSpan lockTtl, CancellationToken cancellationToken = default);
    Task MarkCompletedAsync<T>(string tenantId, string idempotencyKey, T response, TimeSpan retentionTtl, CancellationToken cancellationToken = default);
    Task ReleaseOnFailureAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken = default);
}

public sealed class RedisIdempotencyService(IConnectionMultiplexer redis) : IIdempotencyService
{
    private const string AcquireLuaScript = """
        local current = redis.call('GET', KEYS[1])
        if not current then
            redis.call('SET', KEYS[1], 'IN_PROGRESS', 'EX', tonumber(ARGV[1]))
            return {1, ''}
        end
        if current == 'IN_PROGRESS' then
            return {2, ''}
        end
        return {3, current}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string BuildKey(string tenantId, string idempotencyKey) =>
        $"benny:idempotency:{tenantId}:{idempotencyKey}";

    public async Task<IdempotencyAcquireResult> TryAcquireAsync(
        string tenantId,
        string idempotencyKey,
        TimeSpan lockTtl,
        CancellationToken cancellationToken = default)
    {
        var db = redis.GetDatabase();
        var key = BuildKey(tenantId, idempotencyKey);
        var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
            AcquireLuaScript,
            [key],
            [(int)lockTtl.TotalSeconds]);

        if (result is null || result.Length < 2)
        {
            return new IdempotencyAcquireResult(IdempotencyState.Acquired, null);
        }

        var code = (int)result[0];
        var payload = result[1].ToString();

        return code switch
        {
            1 => new IdempotencyAcquireResult(IdempotencyState.Acquired, null),
            2 => new IdempotencyAcquireResult(IdempotencyState.InProgress, null),
            _ => new IdempotencyAcquireResult(IdempotencyState.Completed, payload)
        };
    }

    public async Task MarkCompletedAsync<T>(
        string tenantId,
        string idempotencyKey,
        T response,
        TimeSpan retentionTtl,
        CancellationToken cancellationToken = default)
    {
        var db = redis.GetDatabase();
        var key = BuildKey(tenantId, idempotencyKey);
        var serialized = JsonSerializer.Serialize(response, JsonOptions);
        await db.StringSetAsync(key, serialized, retentionTtl);
    }

    public async Task ReleaseOnFailureAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var db = redis.GetDatabase();
        await db.KeyDeleteAsync(BuildKey(tenantId, idempotencyKey));
    }
}
