# Architecture Deep-Dive — Project Benny

This document explains the **domain rationale**, **service responsibilities**, **data streaming vs. batch processing trade-offs**, **caching topology**, and **polyglot persistence strategy** behind **Project Benny**.

---

## 1. Domain Overview: Why Build Project Benny?

Modern cloud-native platforms in fintech, digital commerce, telecommunications, and IoT telemetry face a classic architectural tension:

1. **Strict Transactional Fast Path (`<15ms P99`)**:
   - Every incoming transaction or event must be authenticated, rate-limited, deduplicated (`Idempotency-Key`), evaluated for fraud/risk across multiple rules and velocity counters, and committed to an authoritative ACID ledger with **zero data loss** and **99.99%+ availability**.
2. **Massive Analytical & Telemetry Data Volume (Petabyte Scale)**:
   - Every transaction generates rich audit logs, time-series telemetry, rolling risk metrics, and analytical projections that must be queryable in real time across billions of rows—without ever degrading the `<15ms` transactional fast path.

**Project Benny** resolves this tension by combining **CQRS (Command Query Responsibility Segregation)**, **Two-Tier Near-Caching**, the **Transactional Outbox Pattern**, **Real-Time Event Streaming with Micro-Batching**, and **Polyglot Persistence** on **.NET 10**, **C# 14**, and **ASP.NET Core 10 Web API Controllers**.

---

## 2. Deep-Dive into the 6 Microservices

### 2.1 `Benny.EdgeGateway` (Port `8080`)
- **What it is**: The perimeter API Gateway and traffic shield.
- **Why it is here**: Protects internal microservices from unauthenticated requests, DDoS spikes, and noisy-neighbor tenants.
- **Key Features**:
  - TLS 1.3 termination and OAuth2/JWT validation.
  - **Atomic Redis Lua Token-Bucket Rate Limiter**: Evaluates tenant/IP rate limits in a single network round-trip (`<0.5ms`) via `StackExchange.Redis`, returning standardized RFC 9457 `HTTP 429 Too Many Requests` `ProblemDetails` payloads when burst limits are exceeded.
  - Propagates W3C `traceparent`, `X-Correlation-Id`, `X-Tenant-Id`, and `Idempotency-Key` headers downstream via ASP.NET Core 10 Web API Controllers / YARP.

### 2.2 `Benny.IngestionService` (Port `8081`)
- **What it is**: The frontline synchronous command coordinator.
- **Why it is here**: Guarantees **exactly-once command acceptance** even when mobile/web clients or partner gateways aggressively retry timed-out requests during network blips.
- **Key Features**:
  - **Idempotency Barrier**: Checks `L1 IMemoryCache -> L2 Redis` (`SET key IN_PROGRESS NX EX 60`). Duplicate retries immediately receive the cached response (`<1ms`) without re-running risk checks or double-charging the ledger.
  - **Fast-Path Orchestration**: Exposes strongly-typed ASP.NET Core 10 Web API Controllers (`[ApiController]`, `ControllerBase`) and calls `Benny.RiskEngine` and `Benny.LedgerService` over HTTP/2 (`SocketsHttpHandler`) using typed `HttpClient` instances protected by **Polly v8 (`Microsoft.Extensions.Http.Resilience`)** circuit breakers and concurrency limiters.

### 2.3 `Benny.RiskEngine` (Port `8082`)
- **What it is**: The sub-5ms fraud, compliance, and routing decision engine.
- **Why it is here**: Prevents fraudulent, over-limit, or anomalous transactions *before* any ledger state is mutated.
- **Key Features**:
  - **Structured Parallel Fan-Out (`Task.WhenAll` + linked 5ms `CancellationTokenSource`)**: Fans out 3 parallel async checks with automatic `AsyncLocal<RequestContext>` inheritance and a strict `5ms` deadline:
    1. **Account Velocity Check**: Queries/increments rolling spending counters in Redis (`L2`).
    2. **Tenant Rule Graph Evaluation**: Evaluates tenant-specific compliance rules cached in pod-local **`IMemoryCache` (`L1`)** (`<100µs`).
    3. **Heuristic Anomaly Scorer**: Evaluates transaction amount, geo-velocity, and device fingerprint patterns.
  - Returns an exhaustive C# 14 discriminated-union `abstract record RiskDecision` (`Approved`, `Rejected`, `ManualReviewRequired`).

### 2.4 `Benny.LedgerService` (Port `8083`)
- **What it is**: The authoritative ACID system of record for financial balances and transaction state.
- **Why it is here**: Guarantees strict accounting integrity and **eliminates dual-write bugs** between the database and Apache Kafka.
- **Key Features**:
  - Updates `account_balances` (using optimistic concurrency `version` checks to prevent lost updates under concurrency) and inserts immutable `ledger_entries` in **PostgreSQL 17** via `EF Core 10` / `Dapper` (`Npgsql`).
  - **Transactional Outbox Pattern**: Inserts an `OutboxEvent` into `outbox_events` within the **exact same PostgreSQL ACID transaction** (`NpgsqlTransaction`). An asynchronous Outbox Relay `BackgroundService` (`FOR UPDATE SKIP LOCKED`) or Debezium CDC streams committed events to Apache Kafka with `Acks.All`.

### 2.5 `Benny.StreamProcessor` (Port `8084`)
- **What it is**: The high-throughput asynchronous Kafka consumer and polyglot data router.
- **Why it is here**: Decouples heavy analytical/time-series writes from the synchronous checkout path and scales elastically with event volume.
- **Key Features**:
  - **C# 14 `System.Threading.Channels` & `.Chunk(100)`**: Groups incoming Kafka records into micro-batches for high-efficiency bulk database writes with backpressure control.
  - **Parallel Polyglot Sink Fan-Out**: Uses `Task.WhenAll` to write concurrently to **ScyllaDB**, **ClickHouse**, **Redis** (live rolling KPIs), and **Apache Iceberg**.
  - **Non-Blocking Retries & Dead Letter Queue (`benny.dlq`)**: Routes transient failures to backoff retry topics (`1s -> 5s -> 30s`) and isolates malformed poison-pill records into `benny.dlq` with full OpenTelemetry trace headers.

### 2.6 `Benny.QueryService` (Port `8085`)
- **What it is**: The dedicated CQRS Read-Model Web API Controller service for dashboards, reporting, and history lookups.
- **Why it is here**: Ensures complex analytical queries and historical scans never touch or slow down the live PostgreSQL write ledger.
- **Key Features**:
  - Serves sub-second tenant analytics from **ClickHouse**, time-series telemetry trails from **ScyllaDB**, and hot projections from the **Two-Tier Near-Cache (`IMemoryCache L1 + Redis L2`)**.

---

## 3. Why Data Streaming Instead of Traditional Batch Processing?

### 3.1 The Architectural Flaws of Batch-Only Processing for Real-Time Systems
In legacy architectures, operational databases (PostgreSQL/SQL Server/Oracle) handle writes all day, and a scheduled **Batch Job** (cron, SSIS, Spark, Hangfire/Quartz batch) runs every hour or at midnight to extract data, compute fraud metrics, and populate reporting tables.

At cloud-native scale, batch-only processing fails in four ways:
1. **Delayed Fraud & Risk Detection**: If a compromised account executes 500 transactions/sec, an hourly batch job discovers the velocity spike up to 60 minutes too late. Real-time streaming updates velocity counters in **under 100ms**, stopping fraud on the very next request.
2. **Spiky "Batch Storms" on Operational Databases**: Scanning millions of rows from PostgreSQL every hour causes massive I/O and CPU spikes that degrade P99 latency for live users. Streaming extracts changes incrementally as they happen.
3. **Downstream Coupling Without a Buffer**: Without a durable event stream (Apache Kafka), any traffic spike must either be processed synchronously (crashing downstream stores) or dropped. Kafka acts as a **durable shock absorber**, buffering millions of events on disk while **KEDA** scales out `Benny.StreamProcessor` pods.
4. **Coarse Failure Recovery**: When a multi-hour batch job fails near completion, recovery requires expensive re-runs or manual cleanup. Kafka consumer groups track exact partition offsets and isolate individual poison-pill records to `benny.dlq`.

### 3.2 How Benny Combines Streaming, Micro-Batching, and Cold Batch Lakehouse
Rather than treating Streaming and Batch as mutually exclusive, **Project Benny** uses a **3-tier hybrid data pipeline**:

1. **Continuous Event Streaming (`Apache Kafka KRaft`)**:
   - Events are emitted immediately via the Transactional Outbox for sub-second end-to-end propagation.
2. **In-Stream Micro-Batching (`System.Threading.Channels` + `.Chunk(100)`)**:
   - Columnar and wide-column databases (**ClickHouse** and **ScyllaDB**) perform poorly on single-row inserts but excel at batch inserts.
   - `Benny.StreamProcessor` consumes continuous Kafka streams, groups them in memory using bounded `Channel<T>` and `.Chunk(100)`, and flushes micro-batches in parallel—achieving **sub-200ms real-time freshness** with **batch-grade write efficiency**.
3. **Cold Lakehouse Batch Processing (`Apache Iceberg on S3/MinIO`)**:
   - Long-horizon workloads—such as quarterly regulatory audits, historical backfills, and training deep-learning fraud models over 12 months of data—run as true batch jobs over **Apache Iceberg Parquet tables**, completely isolated from live production databases.

---

## 4. Polyglot Persistence Matrix: Why Each Database Was Chosen

| Data Store | Role in Project Benny | Access Pattern | Why This Engine Instead of Others? |
| :--- | :--- | :--- | :--- |
| **`IMemoryCache` / `HybridCache` (L1)** | Pod-local in-memory hot cache | Read-heavy (`<100µs`), invalidated via Redis Pub/Sub | Zero network serialization overhead for hot tenant rules and configs. |
| **Redis 8 / Valkey (L2 Cache)** | Distributed cache, idempotency locks, rate limits, velocity counters | Atomic Lua scripts, `MGET`/`INCRBYFLOAT` (`<1ms`) | Shared sub-millisecond state across 100+ horizontally scaled pods. |
| **PostgreSQL 17** | ACID Transaction Ledger & Outbox | Strict ACID transactions, optimistic concurrency, `SKIP LOCKED` | Unmatched relational integrity and transactional guarantees for financial balances. |
| **ScyllaDB / Cassandra** | High-velocity raw telemetry & audit log | Massive write IOPS, point-in-time & partition range reads by `(tenant_id, day)` | Masterless wide-column ring scales write throughput linearly across nodes without lock contention. |
| **ClickHouse** | Real-time OLAP analytics engine | Append-only micro-batches; heavy `SUM`/`COUNT`/`quantile(0.99)` scans | Columnar compression and vectorized execution scan billions of rows in milliseconds. |
| **Apache Iceberg (S3/MinIO)** | Cold data lakehouse | Parquet compaction, time-travel batch queries, ML training | Object storage cost-efficiency for petabytes of multi-year historical data. |
