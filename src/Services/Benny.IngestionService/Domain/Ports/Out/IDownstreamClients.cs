using Benny.Common.Domain.Models;

namespace Benny.IngestionService.Domain.Ports.Out;

public interface IRiskEngineClient
{
    Task<RiskDecision> EvaluateAsync(RiskEvaluationRequest request, CancellationToken cancellationToken);
}

public interface ILedgerServiceClient
{
    Task<LedgerEntryDto> RecordTransactionAsync(RecordLedgerRequest request, CancellationToken cancellationToken);
}
