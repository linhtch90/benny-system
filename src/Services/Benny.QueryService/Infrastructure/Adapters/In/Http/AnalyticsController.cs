using Benny.Caching;
using Benny.Common.Domain.Models;
using Benny.QueryService.Infrastructure.Adapters.Out;
using Microsoft.AspNetCore.Mvc;

namespace Benny.QueryService.Infrastructure.Adapters.In.Http;

[ApiController]
[Route("api/v1")]
public sealed class AnalyticsController(
    IAnalyticsQueryRepository repository,
    ITwoTierNearCache nearCache) : ControllerBase
{
    [HttpGet("analytics/tenants/{tenantId}/summary")]
    public async Task<ActionResult<AnalyticsSummaryDto>> GetTenantSummary(
        [FromRoute] string tenantId,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"benny:analytics:summary:{tenantId}";
        var summary = await nearCache.GetOrLoadAsync<AnalyticsSummaryDto>(
            cacheKey,
            async ct => await repository.GetTenantSummaryAsync(tenantId, ct),
            l1Ttl: TimeSpan.FromSeconds(5),
            l2Ttl: TimeSpan.FromSeconds(30),
            cancellationToken);

        return Ok(summary);
    }

    [HttpGet("analytics/tenants/{tenantId}/telemetry")]
    public async Task<ActionResult<IReadOnlyList<TelemetryEventPayload>>> GetTenantTelemetry(
        [FromRoute] string tenantId,
        [FromQuery] string? bucketDay,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var day = string.IsNullOrWhiteSpace(bucketDay)
            ? DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")
            : bucketDay;

        var items = await repository.GetTenantTelemetryAsync(tenantId, day, Math.Clamp(limit, 1, 500), cancellationToken);
        return Ok(items);
    }

    [HttpGet("transactions/{txId:guid}")]
    public async Task<ActionResult<LedgerEntryDto>> GetTransaction(
        [FromRoute] Guid txId,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"benny:tx:{txId}";
        var entry = await nearCache.GetOrLoadAsync(
            cacheKey,
            ct => repository.GetTransactionByIdAsync(txId, ct),
            l1Ttl: TimeSpan.FromMinutes(1),
            l2Ttl: TimeSpan.FromMinutes(10),
            cancellationToken);

        return entry is null ? NotFound() : Ok(entry);
    }
}
