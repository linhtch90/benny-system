using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace Benny.Outbox;

public sealed record OutboxEventRecord(
    Guid Id,
    string AggregateType,
    string AggregateId,
    string EventType,
    string Topic,
    string Payload,
    string? TraceParent,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt);

public interface IOutboxPublisher
{
    Task EnqueueAsync<T>(
        DbConnection connection,
        DbTransaction transaction,
        string aggregateType,
        string aggregateId,
        string eventType,
        string topic,
        T payload,
        CancellationToken cancellationToken = default);
}

public interface IOutboxRelayStore
{
    Task<IReadOnlyList<OutboxEventRecord>> ClaimBatchAsync(int batchSize, CancellationToken cancellationToken = default);
    Task MarkPublishedAsync(IEnumerable<Guid> eventIds, CancellationToken cancellationToken = default);
}

public sealed class NpgsqlOutboxStore(NpgsqlDataSource dataSource) : IOutboxPublisher, IOutboxRelayStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task EnqueueAsync<T>(
        DbConnection connection,
        DbTransaction transaction,
        string aggregateType,
        string aggregateId,
        string eventType,
        string topic,
        T payload,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO outbox_events (id, aggregate_type, aggregate_id, event_type, topic, payload, trace_parent, created_at)
            VALUES (@Id, @AggregateType, @AggregateId, @EventType, @Topic, @Payload::jsonb, @TraceParent, @CreatedAt)
            """;

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var record = new
        {
            Id = Guid.CreateVersion7(),
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = eventType,
            Topic = topic,
            Payload = json,
            TraceParent = Activity.Current?.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await connection.ExecuteAsync(new CommandDefinition(sql, record, transaction: transaction, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<OutboxEventRecord>> ClaimBatchAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id AS Id,
                   aggregate_type AS AggregateType,
                   aggregate_id AS AggregateId,
                   event_type AS EventType,
                   topic AS Topic,
                   payload::text AS Payload,
                   trace_parent AS TraceParent,
                   created_at AS CreatedAt,
                   published_at AS PublishedAt
            FROM outbox_events
            WHERE published_at IS NULL
            ORDER BY created_at ASC
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED
            """;

        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await conn.QueryAsync<OutboxEventRecord>(new CommandDefinition(sql, new { BatchSize = batchSize }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task MarkPublishedAsync(IEnumerable<Guid> eventIds, CancellationToken cancellationToken = default)
    {
        var ids = eventIds.ToArray();
        if (ids.Length == 0) return;

        const string sql = """
            UPDATE outbox_events
            SET published_at = @PublishedAt
            WHERE id = ANY(@Ids)
            """;

        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { PublishedAt = DateTimeOffset.UtcNow, Ids = ids }, cancellationToken: cancellationToken));
    }
}
