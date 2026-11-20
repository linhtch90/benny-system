using Benny.Caching;
using Benny.Common.Domain.Models;
using Benny.StreamProcessor.Application;
using Cassandra;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Benny.StreamProcessor.Infrastructure.Sinks;

public sealed class ScyllaTelemetrySink(
    IConfiguration configuration,
    ILogger<ScyllaTelemetrySink> logger) : IPolyglotSink, IDisposable
{
    private readonly Lazy<Cassandra.ISession> _session = new(() =>
    {
        var contactPoint = configuration["Scylla:ContactPoint"] ?? "localhost";
        var cluster = Cluster.Builder().AddContactPoint(contactPoint).Build();
        return cluster.Connect("benny_telemetry");
    });

    public async Task WriteBatchAsync(IReadOnlyList<TransactionValidatedEvent> batch, CancellationToken cancellationToken)
    {
        var session = _session.Value;
        var prepared = await session.PrepareAsync(
            "INSERT INTO events_by_tenant_day (tenant_id, bucket_day, event_time, tx_id, account_id, amount, currency, risk_score, status) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)");

        var batchStmt = new BatchStatement();
        foreach (var evt in batch)
        {
            var bucketDay = evt.OccurredAt.ToString("yyyy-MM-dd");
            batchStmt.Add(prepared.Bind(
                evt.TenantId,
                bucketDay,
                evt.OccurredAt.UtcDateTime,
                evt.TransactionId,
                evt.AccountId,
                (double)evt.Amount,
                evt.Currency,
                (double)evt.RiskScore,
                evt.Status));
        }

        await session.ExecuteAsync(batchStmt);
        logger.LogDebug("Wrote {Count} events to ScyllaDB", batch.Count);
    }

    public void Dispose()
    {
        if (_session.IsValueCreated)
        {
            _session.Value.Dispose();
        }
    }
}

public sealed class ClickHouseAnalyticsSink(
    IConfiguration configuration,
    ILogger<ClickHouseAnalyticsSink> logger) : IPolyglotSink
{
    private readonly string _connectionString = configuration.GetConnectionString("ClickHouse")
        ?? "Host=localhost;Port=8123;Database=benny_analytics";

    public async Task WriteBatchAsync(IReadOnlyList<TransactionValidatedEvent> batch, CancellationToken cancellationToken)
    {
        await using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var bulkCopy = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = "transaction_events",
            BatchSize = batch.Count
        };

        await bulkCopy.InitAsync();

        var rows = batch.Select(evt => new object[]
        {
            evt.EventId,
            evt.TransactionId,
            evt.TenantId,
            evt.AccountId,
            evt.Amount,
            evt.Currency,
            evt.MerchantId,
            evt.RiskScore,
            evt.Status,
            evt.OccurredAt.UtcDateTime
        });

        await bulkCopy.WriteToServerAsync(rows, cancellationToken);
        logger.LogDebug("Wrote {Count} events to ClickHouse", batch.Count);
    }
}

public sealed class RedisMetricsSink(
    IConnectionMultiplexer redis,
    ITwoTierNearCache nearCache) : IPolyglotSink
{
    public async Task WriteBatchAsync(IReadOnlyList<TransactionValidatedEvent> batch, CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        var tasks = new List<Task>(batch.Count * 2);

        foreach (var evt in batch)
        {
            var metricsKey = $"benny:metrics:{evt.TenantId}:live";
            tasks.Add(db.HashIncrementAsync(metricsKey, "tx_count", 1));
            tasks.Add(db.HashIncrementAsync(metricsKey, "total_volume", (double)evt.Amount));
            tasks.Add(nearCache.InvalidateAsync($"benny:analytics:summary:{evt.TenantId}", cancellationToken));
        }

        await Task.WhenAll(tasks);
    }
}
