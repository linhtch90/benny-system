using System.Net.Http.Json;
using Benny.Common.Domain.Models;
using Benny.Common.Domain.Serialization;
using Benny.IngestionService.Domain.Ports.Out;

namespace Benny.IngestionService.Infrastructure.Adapters.Out;

public sealed class HttpRiskEngineClient(HttpClient httpClient) : IRiskEngineClient
{
    public async Task<RiskDecision> EvaluateAsync(RiskEvaluationRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/risk/evaluate",
            request,
            BennyJsonContext.Default.RiskEvaluationRequest,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var decision = await response.Content.ReadFromJsonAsync(
            BennyJsonContext.Default.RiskDecision,
            cancellationToken);

        return decision ?? throw new InvalidOperationException("RiskEngine returned null decision.");
    }
}

public sealed class HttpLedgerServiceClient(HttpClient httpClient) : ILedgerServiceClient
{
    public async Task<LedgerEntryDto> RecordTransactionAsync(RecordLedgerRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/ledger/entries",
            request,
            BennyJsonContext.Default.RecordLedgerRequest,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var entry = await response.Content.ReadFromJsonAsync(
            BennyJsonContext.Default.LedgerEntryDto,
            cancellationToken);

        return entry ?? throw new InvalidOperationException("LedgerService returned null entry.");
    }
}
