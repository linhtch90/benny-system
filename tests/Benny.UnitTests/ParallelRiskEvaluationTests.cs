using System.Diagnostics;
using Benny.Common.Domain.Models;
using Benny.RiskEngine.Application.UseCases;
using Benny.RiskEngine.Domain.Models;
using Xunit;

namespace Benny.UnitTests;

public sealed class ParallelRiskEvaluationTests
{
    private sealed class StubVelocityStore(decimal result, TimeSpan delay) : IVelocityStore
    {
        public async Task<decimal> IncrementAndGetHourlySpendAsync(string tenantId, string accountId, decimal amount, CancellationToken cancellationToken)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            return result;
        }
    }

    private sealed class StubRuleStore : ITenantRuleStore
    {
        public ValueTask<TenantRiskRules> GetRulesAsync(string tenantId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new TenantRiskRules(
                tenantId,
                MaxSingleTransactionAmount: 10_000m,
                MaxHourlyVelocityAmount: 50_000m,
                BlockedCountries: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "KP" }));
    }

    private sealed class StubAnomalyScorer(decimal score) : IAnomalyScorer
    {
        public Task<decimal> ScoreAsync(RiskEvaluationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(score);
    }

    [Fact]
    public async Task EvaluateAsync_ApprovesValidTransactionWithinBudget()
    {
        var svc = new ParallelRiskEvaluationService(
            new StubVelocityStore(500m, TimeSpan.Zero),
            new StubRuleStore(),
            new StubAnomalyScorer(0.05m),
            new ActivitySource("test"));

        var decision = await svc.EvaluateAsync(
            new RiskEvaluationRequest("t1", "acc-1", 100m, "USD", "m-1", "US", "dev-1"),
            CancellationToken.None);

        var approved = Assert.IsType<RiskDecision.Approved>(decision);
        Assert.Equal(0.05m, approved.Score);
    }

    [Fact]
    public async Task EvaluateAsync_CancelsWhenDownstreamExceeds5msBudget()
    {
        var svc = new ParallelRiskEvaluationService(
            new StubVelocityStore(500m, TimeSpan.FromMilliseconds(150)),
            new StubRuleStore(),
            new StubAnomalyScorer(0.05m),
            new ActivitySource("test"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            svc.EvaluateAsync(
                new RiskEvaluationRequest("t1", "acc-1", 100m, "USD", "m-1", "US", "dev-1"),
                CancellationToken.None));
    }
}
