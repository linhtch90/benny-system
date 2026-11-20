using System.Text.Json.Serialization;
using Benny.Common.Domain.Models;

namespace Benny.Common.Domain.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TransactionCommand))]
[JsonSerializable(typeof(IngestionAcceptedResponse))]
[JsonSerializable(typeof(RiskEvaluationRequest))]
[JsonSerializable(typeof(RiskDecision))]
[JsonSerializable(typeof(RiskDecision.Approved))]
[JsonSerializable(typeof(RiskDecision.Rejected))]
[JsonSerializable(typeof(RiskDecision.ManualReviewRequired))]
[JsonSerializable(typeof(RecordLedgerRequest))]
[JsonSerializable(typeof(LedgerEntryDto))]
[JsonSerializable(typeof(TransactionValidatedEvent))]
[JsonSerializable(typeof(TelemetryEventPayload))]
[JsonSerializable(typeof(IReadOnlyList<TelemetryEventPayload>))]
[JsonSerializable(typeof(AnalyticsSummaryDto))]
public partial class BennyJsonContext : JsonSerializerContext;
