using Benny.Caching;
using Benny.Common.Domain.Models;
using Benny.RiskEngine.Domain.Models;
using StackExchange.Redis;

namespace Benny.RiskEngine.Infrastructure.Adapters.Out;

public sealed class RedisVelocityStore(IConnectionMultiplexer redis) : IVelocityStore
{
    public async Task<decimal> IncrementAndGetHourlySpendAsync(
        string tenantId,
        string accountId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        var hourBucket = DateTimeOffset.UtcNow.ToString("yyyyMMddHH");
        var key = $"benny:velocity:{tenantId}:{accountId}:{hourBucket}";

        var updated = await db.StringIncrementAsync(key, (double)amount);
        await db.KeyExpireAsync(key, TimeSpan.FromHours(2), CommandFlags.FireAndForget);
        return (decimal)updated;
    }
}

public sealed class CachedTenantRuleStore(ITwoTierNearCache nearCache) : ITenantRuleStore
{
    public async ValueTask<TenantRiskRules> GetRulesAsync(string tenantId, CancellationToken cancellationToken)
    {
        var cacheKey = $"benny:rules:{tenantId}";
        var rules = await nearCache.GetOrLoadAsync(
            cacheKey,
            _ => Task.FromResult<TenantRiskRules?>(new TenantRiskRules(
                tenantId,
                MaxSingleTransactionAmount: 50_000m,
                MaxHourlyVelocityAmount: 250_000m,
                BlockedCountries: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "KP", "IR" })),
            l1Ttl: TimeSpan.FromSeconds(30),
            l2Ttl: TimeSpan.FromMinutes(10),
            cancellationToken);

        return rules!;
    }
}

public sealed class HeuristicAnomalyScorer : IAnomalyScorer
{
    public Task<decimal> ScoreAsync(RiskEvaluationRequest request, CancellationToken cancellationToken)
    {
        decimal score = 0.02m;
        if (request.Amount > 10_000m) score += 0.25m;
        if (string.IsNullOrWhiteSpace(request.DeviceFingerprint)) score += 0.10m;
        if (request.Amount > 40_000m && string.IsNullOrWhiteSpace(request.DeviceFingerprint)) score += 0.45m;
        return Task.FromResult(Math.Min(score, 0.99m));
    }
}
