# Phase 2 Prompt — Implement Core Microservices (Fast Path)

```markdown
Act as `dotnet-microservice-engineer` and activate the `scaffold-dotnet-service` skill.

Task: Implement the synchronous low-latency (<15ms P99) microservices in `src/Services/` using ASP.NET Core 10 Web API Controllers (`[ApiController]` — do not use Minimal APIs):
1. `src/Services/Benny.EdgeGateway`:
   - ASP.NET Core 10 Web API Controller / YARP Gateway with Redis Lua token-bucket rate limiting, JWT validation, and Polly v8 circuit breaking.
2. `src/Services/Benny.IngestionService`:
   - High-throughput Web API Controller & gRPC ingestion endpoint running on Kestrel HTTP/2.
   - Redis + `IMemoryCache` idempotency barrier (`Idempotency-Key` header).
   - Orchestrates synchronous call to `Benny.RiskEngine` and command persistence to `Benny.LedgerService`.
3. `src/Services/Benny.RiskEngine`:
   - Uses `Task.WhenAll` with a linked `CancellationTokenSource` deadline to run parallel velocity, geo-ip, and rule checks within a strict 5ms budget.
4. `src/Services/Benny.LedgerService`:
   - Persists transactions into partitioned PostgreSQL 17 tables and writes `OutboxEvent` records atomically in the same ACID transaction (`NpgsqlTransaction`).
5. Include xUnit v3 + Testcontainers integration tests verifying idempotency, parallel deadline cancellation, and transactional outbox persistence.
```
