using System.Diagnostics;
using Benny.Common.Domain.Context;
using Benny.Common.Domain.Models;
using Benny.RiskEngine.Domain.Models;

namespace Benny.RiskEngine.Application.UseCases;

public sealed class ParallelRiskEvaluationService(
    IVelocityStore velocityStore,
    ITenantRuleStore tenantRuleStore,
    IAnomalyScorer anomalyScorer,
    ActivitySource activitySource)
{
    private static readonly TimeSpan EvaluationBudget = TimeSpan.FromMilliseconds(5);

    public async Task<RiskDecision> EvaluateAsync(RiskEvaluationRequest request, CancellationToken cancellationToken)
    {
        using var activity = activitySource.StartActivity("RiskEngine.ParallelEvaluate");
        var correlationId = RequestContextHolder.Current?.CorrelationId;
        activity?.SetTag("benny.correlation_id", correlationId);

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetCts.CancelAfter(EvaluationBudget);
        var ct = budgetCts.Token;

        var velocityTask = velocityStore.IncrementAndGetHourlySpendAsync(request.TenantId, request.AccountId, request.Amount, ct);
        var rulesTask = tenantRuleStore.GetRulesAsync(request.TenantId, ct).AsTask();
        var anomalyTask = anomalyScorer.ScoreAsync(request, ct);

        await Task.WhenAll(velocityTask, rulesTask, anomalyTask);

        var hourlySpend = await velocityTask;
        var rules = await rulesTask;
        var anomalyScore = await anomalyTask;

        if (!string.IsNullOrWhiteSpace(request.CountryCode) &&
            rules.BlockedCountries.Contains(request.CountryCode.ToUpperInvariant()))
        {
            return new RiskDecision.Rejected(0.99m, $"Country '{request.CountryCode}' is blocked by tenant policy.", "BLOCKED_COUNTRY");
        }

        if (request.Amount > rules.MaxSingleTransactionAmount)
        {
            return new RiskDecision.Rejected(0.95m, $"Amount {request.Amount} exceeds single-tx limit {rules.MaxSingleTransactionAmount}.", "MAX_TX_AMOUNT");
        }

        if (hourlySpend > rules.MaxHourlyVelocityAmount)
        {
            return new RiskDecision.Rejected(0.90m, $"Hourly velocity {hourlySpend} exceeds limit {rules.MaxHourlyVelocityAmount}.", "VELOCITY_LIMIT");
        }

        if (anomalyScore >= 0.75m)
        {
            return new RiskDecision.ManualReviewRequired(anomalyScore, "Elevated heuristic anomaly score.");
        }

        return new RiskDecision.Approved(anomalyScore);
    }
}
