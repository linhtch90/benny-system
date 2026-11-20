namespace Benny.Common.Domain.Context;

public readonly record struct RequestContext(
    string CorrelationId,
    string TenantId,
    string? IdempotencyKey,
    DateTimeOffset ReceivedAt)
{
    public static RequestContext Create(string? correlationId, string? tenantId, string? idempotencyKey = null) =>
        new(
            string.IsNullOrWhiteSpace(correlationId) ? Guid.CreateVersion7().ToString("N") : correlationId,
            string.IsNullOrWhiteSpace(tenantId) ? "default-tenant" : tenantId,
            string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            DateTimeOffset.UtcNow);
}

public static class RequestContextHolder
{
    private static readonly AsyncLocal<RequestContext?> ContextLocal = new();

    public static RequestContext? Current
    {
        get => ContextLocal.Value;
        set => ContextLocal.Value = value;
    }

    public static IDisposable Push(RequestContext context)
    {
        var previous = ContextLocal.Value;
        ContextLocal.Value = context;
        return new ContextScope(previous);
    }

    private sealed class ContextScope(RequestContext? previous) : IDisposable
    {
        public void Dispose() => ContextLocal.Value = previous;
    }
}
