using System.Net;
using System.Net.Http.Json;
using Benny.Common.Domain.Context;
using Benny.Common.Domain.Models;
using Benny.EdgeGateway.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace Benny.EdgeGateway.Infrastructure.Adapters.In.Http;

[ApiController]
[Route("api/v1")]
public sealed class EdgeGatewayController(
    ITokenBucketRateLimiter rateLimiter,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    [HttpPost("transactions")]
    [HttpPost("events")]
    public async Task<IActionResult> SubmitTransaction(
        [FromBody] TransactionCommand command,
        CancellationToken cancellationToken)
    {
        var tenantId = RequestContextHolder.Current?.TenantId ?? "default-tenant";
        var limit = await rateLimiter.TryConsumeAsync(tenantId, cancellationToken: cancellationToken);
        Response.Headers["X-RateLimit-Remaining"] = limit.RemainingTokens.ToString();

        if (!limit.Allowed)
        {
            Response.Headers.RetryAfter = limit.RetryAfterSeconds.ToString();
            return Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Rate Limit Exceeded",
                detail: $"Tenant '{tenantId}' exceeded token-bucket burst capacity.",
                type: "https://benny.io/errors/rate_limit_exceeded");
        }

        var client = httpClientFactory.CreateClient("IngestionClient");
        using var response = await client.PostAsJsonAsync("/api/v1/ingestion/transactions", command, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = body,
            ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json"
        };
    }

    [HttpGet("analytics/tenants/{tenantId}/summary")]
    public async Task<IActionResult> GetTenantSummary(
        [FromRoute] string tenantId,
        CancellationToken cancellationToken)
    {
        var limit = await rateLimiter.TryConsumeAsync(tenantId, cancellationToken: cancellationToken);
        Response.Headers["X-RateLimit-Remaining"] = limit.RemainingTokens.ToString();

        if (!limit.Allowed)
        {
            return Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Rate Limit Exceeded",
                detail: $"Tenant '{tenantId}' exceeded token-bucket burst capacity.");
        }

        var client = httpClientFactory.CreateClient("QueryClient");
        using var response = await client.GetAsync($"/api/v1/analytics/tenants/{Uri.EscapeDataString(tenantId)}/summary", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = body,
            ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json"
        };
    }
}
