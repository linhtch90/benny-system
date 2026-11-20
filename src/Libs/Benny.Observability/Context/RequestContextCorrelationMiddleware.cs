using System.Diagnostics;
using Benny.Common.Domain.Context;
using Microsoft.AspNetCore.Http;

namespace Benny.Observability.Context;

public static class BennyHeaders
{
    public const string CorrelationId = "X-Correlation-Id";
    public const string TenantId = "X-Tenant-Id";
    public const string IdempotencyKey = "Idempotency-Key";
}

public sealed class RequestContextCorrelationMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var correlationId = context.Request.Headers[BennyHeaders.CorrelationId].FirstOrDefault();
        var tenantId = context.Request.Headers[BennyHeaders.TenantId].FirstOrDefault();
        var idempotencyKey = context.Request.Headers[BennyHeaders.IdempotencyKey].FirstOrDefault();

        var reqContext = RequestContext.Create(correlationId, tenantId, idempotencyKey);
        context.Response.Headers[BennyHeaders.CorrelationId] = reqContext.CorrelationId;

        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("benny.correlation_id", reqContext.CorrelationId);
            activity.SetTag("benny.tenant_id", reqContext.TenantId);
            if (reqContext.IdempotencyKey is not null)
            {
                activity.SetTag("benny.idempotency_key", reqContext.IdempotencyKey);
            }
        }

        using (RequestContextHolder.Push(reqContext))
        {
            await next(context);
        }
    }
}

public sealed class RequestContextPropagationHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var current = RequestContextHolder.Current;
        if (current is { } ctx)
        {
            request.Headers.TryAddWithoutValidation(BennyHeaders.CorrelationId, ctx.CorrelationId);
            request.Headers.TryAddWithoutValidation(BennyHeaders.TenantId, ctx.TenantId);
            if (!string.IsNullOrWhiteSpace(ctx.IdempotencyKey))
            {
                request.Headers.TryAddWithoutValidation(BennyHeaders.IdempotencyKey, ctx.IdempotencyKey);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
