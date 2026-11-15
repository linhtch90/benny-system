# AGENTS.md — Benny Cloud-Native System

Welcome to **Project Benny**, an enterprise-grade, ultra-low latency, high-throughput, cloud-native distributed event & transaction processing platform built on the modern **.NET 10** and **C# 14** stack.

This file serves as the root rulebook and architectural compass for all AI coding agents operating in this repository.

---

## 1. Core Technology Stack & Versions (Strict Enforcement)

- **Language & Runtime**: **C# 14** on **.NET 10 (LTS)** (`net10.0`)
  - Use **Non-Blocking Async/Await I/O** (`Task<T>` / `ValueTask<T>`) on the .NET 10 ThreadPool and Kestrel HTTP/2 & HTTP/3 pipelines for high-throughput I/O operations.
  - Use **Structured Parallel Fan-Out** (`Task.WhenAll` combined with linked `CancellationTokenSource` deadlines) for parallel sub-task orchestration.
  - Use **`AsyncLocal<RequestContext>`** and **`System.Diagnostics.Activity.Current`** for zero-friction propagation of security, tenant, and W3C tracing contexts across async continuations.
  - Use **Records** (`record` / `readonly record struct`), **Discriminated Union Hierarchies** (`abstract record` with exhaustive `switch` expressions), **Primary Constructors**, **Collection Expressions**, and **`System.Text.Json` Source Generators** (`[JsonSerializable]`).
  - Runtime Tuning: **Server GC + DATAS (Dynamic Adaptation To Application Sizes)**, **Dynamic PGO (`TieredPGO=1`)**, and **ReadyToRun (R2R) / Native AOT** for sub-millisecond GC pauses under heavy allocation and `<1s` pod startup.
- **Framework**: **ASP.NET Core 10**
  - **Web API Controllers (`[ApiController]`, `ControllerBase`)** — **Do NOT use Minimal APIs (`MapGet`/`MapPost`/`MapGroup`)** for service endpoints; always use strongly-typed Controllers with attribute routing (`[Route]`, `[HttpGet]`, `[HttpPost]`) and `ActionResult<T>`.
  - **YARP (Yet Another Reverse Proxy)**, **gRPC**, and typed `HttpClient` with **`Microsoft.Extensions.Http.Resilience`**.
  - Built-in **OpenAPI 3.1** & **RFC 9457 `ProblemDetails`** (`IProblemDetailsService` & `IExceptionHandler`) for standardized error responses.
  - **OpenTelemetry .NET (`ActivitySource`, `Meter`) + ASP.NET Core Health Checks** for zero-blind-spot observability.
- **Build & Solution System**: **.NET 10 SDK (`BennySystem.sln`, `Directory.Build.props`, `Directory.Packages.props`)**
  - Centralized build configuration via root `Directory.Build.props` (`TargetFramework=net10.0`, `LangVersion=14.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`).
  - Central Package Management (CPM) via `Directory.Packages.props` and SDK pinning via `global.json`.

---

## 2. Cloud-Native Non-Functional Requirements

1. **High Availability (99.99%+ SLA)**:
   - Stateless compute pods deployed across multiple Availability Zones (Multi-AZ).
   - Graceful shutdown (`HostOptions.ShutdownTimeout = 30s`) with Kubernetes readiness/liveness/startup health probes (`/health/live`, `/health/ready`, `/health/startup`).
   - Zero-downtime deployments and schema migrations (EF Core Migrations / DbUp expand-and-contract pattern).
2. **High Performance & Ultra-Low Latency (<15ms P99 Fast Path)**:
   - Two-tier caching strategy: **L1 In-Memory (`IMemoryCache` / `HybridCache`)** + **L2 Distributed (`Redis 8 / Valkey Cluster` via `StackExchange.Redis`)** with single-flight cache stampede protection and Redis Pub/Sub L1 invalidation.
   - Binary serialization (**Protobuf / Avro**) and source-generated JSON for internal service-to-service calls and event streaming (Apache Kafka).
3. **Durability & Resilience to Failures**:
   - **Transactional Outbox Pattern + Debezium CDC / Relay** to guarantee atomic DB state updates and Kafka event publishing (zero dual-write bugs).
   - **Idempotency Keys** enforced at the Edge/Ingestion layer backed by atomic Redis Lua locks.
   - **Polly v8 (`Microsoft.Extensions.Resilience`)** Circuit Breakers, Concurrency Limiters (Bulkheads), Timeouts, Rate Limiters, and Exponential Backoff with Jitter on all remote calls.
   - **Dead Letter Queues (DLQ)** with automated replay and alerting for failed async messages.
4. **Elastic Horizontal Scaling & Massive Data Handling**:
   - **Kubernetes HPA + KEDA** scaling compute pods dynamically based on RPS, CPU/Memory, and **Kafka Consumer Lag**.
   - **Polyglot Persistence**:
     - **PostgreSQL 17 (Citus/Patroni)**: Sharded ACID transactional ledger & outbox (`EF Core 10` + `Dapper` / `Npgsql`).
     - **ScyllaDB / Apache Cassandra**: High-velocity write-heavy raw telemetry & time-series store (`CassandraCSharpDriver`).
     - **ClickHouse**: Real-time OLAP analytics over billions of events (`ClickHouse.Client`).
     - **Apache Iceberg on S3/MinIO**: Cold data lakehouse for historical batch/AI analytics.

---

## 3. Repository AI Customization Map

Before executing complex architectural or implementation tasks, consult the specialized AI assets in `.agents/`:

- **Rules**: [dotnet10-csharp14-standards.md](.agents/rules/dotnet10-csharp14-standards.md)
- **Prompts**:
  - [00-master-project-prompt.md](.agents/prompts/00-master-project-prompt.md)
  - [01-bootstrap-dotnet-monorepo.md](.agents/prompts/01-bootstrap-dotnet-monorepo.md)
  - [02-implement-core-services.md](.agents/prompts/02-implement-core-services.md)
  - [03-implement-data-and-streaming.md](.agents/prompts/03-implement-data-and-streaming.md)
  - [04-load-testing-and-chaos-engineering.md](.agents/prompts/04-load-testing-and-chaos-engineering.md)
- **Agent Personas**:
  - [cloud-architect.md](.agents/agents/cloud-architect.md)
  - [dotnet-microservice-engineer.md](.agents/agents/dotnet-microservice-engineer.md)
  - [data-streaming-specialist.md](.agents/agents/data-streaming-specialist.md)
  - [sre-resilience-auditor.md](.agents/agents/sre-resilience-auditor.md)
- **Skills**:
  - [scaffold-dotnet-service](.agents/skills/scaffold-dotnet-service/SKILL.md)
  - [implement-caching-messaging](.agents/skills/implement-caching-messaging/SKILL.md)
  - [resilience-and-scaling-audit](.agents/skills/resilience-and-scaling-audit/SKILL.md)
  - [plantuml-architecture-sync](.agents/skills/plantuml-architecture-sync/SKILL.md)
- **Architecture Diagrams**:
  - [system-architecture.puml](docs/architecture/system-architecture.puml)
  - [data-flow-sequence.puml](docs/architecture/data-flow-sequence.puml)
  - [scaling-resilience-topology.puml](docs/architecture/scaling-resilience-topology.puml)
