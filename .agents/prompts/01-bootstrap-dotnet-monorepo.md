# Phase 1 Prompt — Bootstrap .NET 10 Monorepo & Local Cloud Infrastructure

```markdown
Act as `dotnet-microservice-engineer` and `cloud-architect`.
Follow the rules in `AGENTS.md` and `.agents/rules/dotnet10-csharp14-standards.md`.

Task: Bootstrap the **Project Benny** .NET 10 multi-project monorepo:
1. Create `BennySystem.sln`, `global.json`, and root `Directory.Build.props` configured for **.NET 10 (`net10.0`)** and **C# 14 (`LangVersion=14.0`)**.
2. Create `Directory.Packages.props` defining all NuGet package versions centrally (ASP.NET Core 10, `Microsoft.Extensions.Resilience`, `Microsoft.Extensions.Caching.Hybrid`, `StackExchange.Redis`, `Confluent.Kafka`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Dapper`, `CassandraCSharpDriver`, `ClickHouse.Client`, `OpenTelemetry.*`, `xunit.v3`, `Testcontainers.*`, `NetArchTest.Rules`).
3. Create shared libraries under `src/Libs/`:
   - `src/Libs/Benny.Common.Domain`: Shared C# 14 records (`TransactionCommand`, `RiskDecision` discriminated union, `RequestContext`), `AsyncLocal` context holders, `BennyJsonContext` source generator, and RFC 9457 `ProblemDetails` helpers.
   - `src/Libs/Benny.Caching`: Reusable Two-Tier Near-Cache (`L1 IMemoryCache` + `L2 StackExchange.Redis` + Pub/Sub invalidation + `RedisIdempotencyService`).
   - `src/Libs/Benny.Outbox`: Reusable Transactional Outbox entity, repository (`FOR UPDATE SKIP LOCKED`), and `BackgroundService` Kafka relay publisher.
   - `src/Libs/Benny.Observability`: Shared registration for OpenTelemetry .NET (`ActivitySource`, `Meter`), correlation middleware, and global `IExceptionHandler`.
4. Create `docker-compose.yml` for local cloud-native infrastructure:
   - PostgreSQL 17, Redis 8 (Valkey), Apache Kafka (KRaft) + Schema Registry, ScyllaDB, ClickHouse, and OpenTelemetry Collector + Grafana LGTM stack.
```
