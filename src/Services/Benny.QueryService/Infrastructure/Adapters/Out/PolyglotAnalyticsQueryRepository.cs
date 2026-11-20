using Benny.Common.Domain.Models;
using Cassandra;
using ClickHouse.Client.ADO;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Benny.QueryService.Infrastructure.Adapters.Out;

public interface IAnalyticsQueryRepository
{
    Task<AnalyticsSummaryDto> GetTenantSummaryAsync(string tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TelemetryEventPayload>> GetTenantTelemetryAsync(string tenantId, string bucketDay, int limit, CancellationToken cancellationToken);
    Task<LedgerEntryDto?> GetTransactionByIdAsync(Guid txId, CancellationToken cancellationToken);
}

public sealed class PolyglotAnalyticsQueryRepository(IConfiguration configuration) : IAnalyticsQueryRepository, IDisposable
{
    private readonly string _clickHouseConn = configuration.GetConnectionString("ClickHouse")
        ?? "Host=localhost;Port=8123;Database=benny_analytics";

    private readonly string _pgConn = configuration.GetConnectionString("Postgres")
        ?? "Host=localhost;Port=5432;Database=benny_ledger;Username=benny;Password=benny_secret";

    private readonly Lazy<Cassandra.ISession> _scyllaSession = new(() =>
    {
        var contactPoint = configuration["Scylla:ContactPoint"] ?? "localhost";
        var cluster = Cluster.Builder().AddContactPoint(contactPoint).Build();
        return cluster.Connect("benny_telemetry");
    });

    public async Task<AnalyticsSummaryDto> GetTenantSummaryAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var conn = new ClickHouseConnection(_clickHouseConn);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                count() AS total_tx,
                coalesce(sum(amount), 0) AS total_vol,
                quantile(0.95)(risk_score) AS p95_risk,
                quantile(0.99)(risk_score) AS p99_risk,
                countIf(status = 'APPROVED') AS approved_cnt,
                countIf(status = 'REJECTED') AS rejected_cnt
            FROM transaction_events
            WHERE tenant_id = @tenantId
            """;

        var param = cmd.CreateParameter();
        param.ParameterName = "tenantId";
        param.Value = tenantId;
        cmd.Parameters.Add(param);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new AnalyticsSummaryDto(
                tenantId,
                Convert.ToInt64(reader.GetValue(0)),
                Convert.ToDecimal(reader.GetValue(1)),
                reader.IsDBNull(2) ? 0.0 : Convert.ToDouble(reader.GetValue(2)),
                reader.IsDBNull(3) ? 0.0 : Convert.ToDouble(reader.GetValue(3)),
                Convert.ToInt64(reader.GetValue(4)),
                Convert.ToInt64(reader.GetValue(5)),
                DateTimeOffset.UtcNow);
        }

        return new AnalyticsSummaryDto(tenantId, 0, 0m, 0.0, 0.0, 0, 0, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<TelemetryEventPayload>> GetTenantTelemetryAsync(
        string tenantId,
        string bucketDay,
        int limit,
        CancellationToken cancellationToken)
    {
        var session = _scyllaSession.Value;
        var stmt = new SimpleStatement(
            "SELECT event_time, tx_id, account_id, amount, currency, risk_score, status FROM events_by_tenant_day WHERE tenant_id = ? AND bucket_day = ? LIMIT ?",
            tenantId, bucketDay, limit);

        var rowSet = await session.ExecuteAsync(stmt);
        var list = new List<TelemetryEventPayload>();

        foreach (var row in rowSet)
        {
            list.Add(new TelemetryEventPayload(
                Guid.Empty,
                row.GetValue<Guid>("tx_id"),
                tenantId,
                row.GetValue<string>("account_id"),
                (decimal)row.GetValue<double>("amount"),
                row.GetValue<string>("currency"),
                (decimal)row.GetValue<double>("risk_score"),
                row.GetValue<string>("status"),
                new DateTimeOffset(row.GetValue<DateTime>("event_time"), TimeSpan.Zero)));
        }

        return list;
    }

    public async Task<LedgerEntryDto?> GetTransactionByIdAsync(Guid txId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT entry_id AS EntryId,
                   transaction_id AS TransactionId,
                   tenant_id AS TenantId,
                   account_id AS AccountId,
                   amount AS Amount,
                   currency AS Currency,
                   balance_after AS BalanceAfter,
                   created_at AS CreatedAt
            FROM ledger_entries
            WHERE transaction_id = @TxId
            LIMIT 1
            """;

        await using var conn = new NpgsqlConnection(_pgConn);
        return await conn.QueryFirstOrDefaultAsync<LedgerEntryDto>(new CommandDefinition(sql, new { TxId = txId }, cancellationToken: cancellationToken));
    }

    public void Dispose()
    {
        if (_scyllaSession.IsValueCreated)
        {
            _scyllaSession.Value.Dispose();
        }
    }
}
