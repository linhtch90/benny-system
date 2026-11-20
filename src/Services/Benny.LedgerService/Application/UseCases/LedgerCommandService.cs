using System.Diagnostics;
using Benny.Common.Domain.Models;
using Benny.Outbox;
using Dapper;
using Npgsql;

namespace Benny.LedgerService.Application.UseCases;

public sealed class LedgerCommandService(
    NpgsqlDataSource dataSource,
    IOutboxPublisher outboxPublisher,
    ActivitySource activitySource)
{
    public const string ValidatedTopic = "benny.transactions.validated";

    public async Task<LedgerEntryDto> RecordTransactionAsync(
        RecordLedgerRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = activitySource.StartActivity("LedgerService.RecordTransaction");

        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        const string upsertBalanceSql = """
            INSERT INTO account_balances (tenant_id, account_id, balance, currency, version, updated_at)
            VALUES (@TenantId, @AccountId, @InitialDelta, @Currency, 1, @Now)
            ON CONFLICT (tenant_id, account_id)
            DO UPDATE SET
                balance = account_balances.balance + @InitialDelta,
                version = account_balances.version + 1,
                updated_at = @Now
            RETURNING balance;
            """;

        var now = DateTimeOffset.UtcNow;
        var balanceAfter = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
            upsertBalanceSql,
            new
            {
                request.TenantId,
                request.AccountId,
                InitialDelta = request.Amount,
                request.Currency,
                Now = now
            },
            transaction: tx,
            cancellationToken: cancellationToken));

        var entryId = Guid.CreateVersion7();
        const string insertEntrySql = """
            INSERT INTO ledger_entries (entry_id, transaction_id, tenant_id, account_id, amount, currency, balance_after, created_at)
            VALUES (@EntryId, @TransactionId, @TenantId, @AccountId, @Amount, @Currency, @BalanceAfter, @CreatedAt);
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            insertEntrySql,
            new
            {
                EntryId = entryId,
                request.TransactionId,
                request.TenantId,
                request.AccountId,
                request.Amount,
                request.Currency,
                BalanceAfter = balanceAfter,
                CreatedAt = now
            },
            transaction: tx,
            cancellationToken: cancellationToken));

        var validatedEvent = new TransactionValidatedEvent(
            Guid.CreateVersion7(),
            request.TransactionId,
            request.TenantId,
            request.AccountId,
            request.Amount,
            request.Currency,
            request.MerchantId,
            request.RiskScore,
            "APPROVED",
            now);

        await outboxPublisher.EnqueueAsync(
            conn,
            tx,
            aggregateType: "Transaction",
            aggregateId: request.TransactionId.ToString(),
            eventType: nameof(TransactionValidatedEvent),
            topic: ValidatedTopic,
            payload: validatedEvent,
            cancellationToken: cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return new LedgerEntryDto(
            entryId,
            request.TransactionId,
            request.TenantId,
            request.AccountId,
            request.Amount,
            request.Currency,
            balanceAfter,
            now);
    }
}
