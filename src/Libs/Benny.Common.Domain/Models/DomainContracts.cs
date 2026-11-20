using System.Text.Json.Serialization;

namespace Benny.Common.Domain.Models;

public sealed record TransactionCommand(
    string AccountId,
    decimal Amount,
    string Currency,
    string MerchantId,
    string? CountryCode,
    string? DeviceFingerprint,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record IngestionAcceptedResponse(
    Guid TransactionId,
    string Status,
    decimal RiskScore,
    DateTimeOffset ProcessedAt,
    bool IdempotentReplay = false);

public sealed record RiskEvaluationRequest(
    string TenantId,
    string AccountId,
    decimal Amount,
    string Currency,
    string MerchantId,
    string? CountryCode,
    string? DeviceFingerprint);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "decisionType")]
[JsonDerivedType(typeof(Approved), "approved")]
[JsonDerivedType(typeof(Rejected), "rejected")]
[JsonDerivedType(typeof(ManualReviewRequired), "manual_review")]
public abstract record RiskDecision
{
    private RiskDecision() { }

    public abstract decimal Score { get; init; }

    public sealed record Approved(decimal Score, string Reason = "Within risk thresholds") : RiskDecision;
    public sealed record Rejected(decimal Score, string Reason, string RuleCode) : RiskDecision;
    public sealed record ManualReviewRequired(decimal Score, string Reason) : RiskDecision;
}

public sealed record RecordLedgerRequest(
    Guid TransactionId,
    string TenantId,
    string AccountId,
    decimal Amount,
    string Currency,
    string MerchantId,
    decimal RiskScore,
    string CorrelationId);

public sealed record LedgerEntryDto(
    Guid EntryId,
    Guid TransactionId,
    string TenantId,
    string AccountId,
    decimal Amount,
    string Currency,
    decimal BalanceAfter,
    DateTimeOffset CreatedAt);

public sealed record TransactionValidatedEvent(
    Guid EventId,
    Guid TransactionId,
    string TenantId,
    string AccountId,
    decimal Amount,
    string Currency,
    string MerchantId,
    decimal RiskScore,
    string Status,
    DateTimeOffset OccurredAt);

public sealed record TelemetryEventPayload(
    Guid EventId,
    Guid TransactionId,
    string TenantId,
    string AccountId,
    decimal Amount,
    string Currency,
    decimal RiskScore,
    string Status,
    DateTimeOffset OccurredAt);

public sealed record AnalyticsSummaryDto(
    string TenantId,
    long TotalTransactions,
    decimal TotalVolume,
    double P95RiskScore,
    double P99RiskScore,
    long ApprovedCount,
    long RejectedCount,
    DateTimeOffset GeneratedAt);
