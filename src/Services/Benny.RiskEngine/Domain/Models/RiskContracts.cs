using Benny.Common.Domain.Models;

namespace Benny.RiskEngine.Domain.Models;

public sealed record TenantRiskRules(
    string TenantId,
    decimal MaxSingleTransactionAmount,
    decimal MaxHourlyVelocityAmount,
    IReadOnlySet<string> BlockedCountries);

public interface IVelocityStore
{
    Task<decimal> IncrementAndGetHourlySpendAsync(string tenantId, string accountId, decimal amount, CancellationToken cancellationToken);
}

public interface ITenantRuleStore
{
    ValueTask<TenantRiskRules> GetRulesAsync(string tenantId, CancellationToken cancellationToken);
}

public interface IAnomalyScorer
{
    Task<decimal> ScoreAsync(RiskEvaluationRequest request, CancellationToken cancellationToken);
}
