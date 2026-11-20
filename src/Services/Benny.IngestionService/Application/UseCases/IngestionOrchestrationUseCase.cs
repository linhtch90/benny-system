using System.Diagnostics;
using System.Text.Json;
using Benny.Caching;
using Benny.Common.Domain.Context;
using Benny.Common.Domain.Errors;
using Benny.Common.Domain.Models;
using Benny.Common.Domain.Serialization;
using Benny.IngestionService.Domain.Ports.Out;

namespace Benny.IngestionService.Application.UseCases;

public sealed class IngestionOrchestrationUseCase(
    IIdempotencyService idempotencyService,
    IRiskEngineClient riskEngineClient,
    ILedgerServiceClient ledgerServiceClient,
    ActivitySource activitySource)
{
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetentionTtl = TimeSpan.FromHours(24);

    public async Task<IngestionAcceptedResponse> ExecuteAsync(
        TransactionCommand command,
        CancellationToken cancellationToken)
    {
        using var activity = activitySource.StartActivity("IngestionOrchestration.Execute");
        var ctx = RequestContextHolder.Current ?? RequestContext.Create(null, null);

        if (!string.IsNullOrWhiteSpace(ctx.IdempotencyKey))
        {
            var acquire = await idempotencyService.TryAcquireAsync(ctx.TenantId, ctx.IdempotencyKey, LockTtl, cancellationToken);
            if (acquire.State == IdempotencyState.InProgress)
            {
                throw new DuplicateIdempotencyConflictException(ctx.IdempotencyKey);
            }

            if (acquire.State == IdempotencyState.Completed && !string.IsNullOrWhiteSpace(acquire.CachedPayload))
            {
                var cached = JsonSerializer.Deserialize(acquire.CachedPayload, BennyJsonContext.Default.IngestionAcceptedResponse);
                if (cached is not null)
                {
                    return cached with { IdempotentReplay = true };
                }
            }
        }

        try
        {
            var riskRequest = new RiskEvaluationRequest(
                ctx.TenantId,
                command.AccountId,
                command.Amount,
                command.Currency,
                command.MerchantId,
                command.CountryCode,
                command.DeviceFingerprint);

            var decision = await riskEngineClient.EvaluateAsync(riskRequest, cancellationToken);

            var status = decision switch
            {
                RiskDecision.Approved => "APPROVED",
                RiskDecision.ManualReviewRequired => "MANUAL_REVIEW_REQUIRED",
                RiskDecision.Rejected r => throw new RiskRejectedException(r.Reason, r.RuleCode, r.Score),
                _ => throw new InvalidOperationException($"Unhandled RiskDecision type: {decision.GetType().Name}")
            };

            var txId = Guid.CreateVersion7();

            if (decision is RiskDecision.Approved)
            {
                var ledgerReq = new RecordLedgerRequest(
                    txId,
                    ctx.TenantId,
                    command.AccountId,
                    command.Amount,
                    command.Currency,
                    command.MerchantId,
                    decision.Score,
                    ctx.CorrelationId);

                await ledgerServiceClient.RecordTransactionAsync(ledgerReq, cancellationToken);
            }

            var response = new IngestionAcceptedResponse(
                txId,
                status,
                decision.Score,
                DateTimeOffset.UtcNow);

            if (!string.IsNullOrWhiteSpace(ctx.IdempotencyKey))
            {
                await idempotencyService.MarkCompletedAsync(ctx.TenantId, ctx.IdempotencyKey, response, RetentionTtl, cancellationToken);
            }

            return response;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(ctx.IdempotencyKey))
            {
                await idempotencyService.ReleaseOnFailureAsync(ctx.TenantId, ctx.IdempotencyKey, CancellationToken.None);
            }
            throw;
        }
    }
}
