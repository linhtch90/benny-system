# Project Benny — Cloud-Native High-Throughput Event, Risk & Transaction Platform

**Project Benny** is an enterprise-grade, cloud-native distributed platform built on the modern .NET stack (**.NET 10**, **C# 14**, **ASP.NET Core 10 Web API Controllers**). It is engineered to demonstrate industry best practices for **99.99%+ High Availability**, **sub-15ms P99 Fast-Path Latency**, **elastic horizontal scaling**, **durable fault tolerance**, and **petabyte-scale event/data processing**.

---

## 1. What Is This System About? (The Business & Engineering Problem)

**Project Benny** models a **Global Real-Time Financial Transaction, Fraud/Risk Intelligence, and High-Volume Telemetry Engine**—similar to the core transaction & risk platforms behind Stripe, Adyen, or Grab/Uber.

Imagine millions of users, payment terminals, e-commerce checkouts, and IoT/partner gateways submitting financial transactions and telemetry events every second, including massive, unpredictable traffic spikes (such as Black Friday or flash sales).

For every single incoming request, the platform must solve **two fundamentally different problems simultaneously**:

1. **The Synchronous "Fast Path" (Under 15 milliseconds P99)**:
   - Verify the caller is authenticated, within their rate limit, and not sending a duplicate network retry (**Rate Limiting & Idempotency Barrier**).
   - Decide in **under 5 milliseconds** whether the transaction is fraudulent or violates tenant compliance rules (**Real-Time Risk Engine** checking account spending velocity, tenant rule graphs, and geo/amount anomalies in parallel).
   - If approved, debit/credit the account balance and record an immutable double-entry accounting record (**Transaction Ledger**) with **100% ACID accuracy and zero lost events**.
2. **The Asynchronous "Big Data & Event Path" (Millions of Events/sec)**:
   - Stream every validated transaction and raw telemetry event reliably into high-speed analytical and time-series data stores so merchants, risk analysts, and AI models can query **live dashboards, fraud trends, and billion-row historical audit logs** in sub-seconds—without ever slowing down live checkouts.

---

## 2. What Each of the 6 Microservices Does & Why It Exists

| Service | Port | Business Role | Why It Exists in the Architecture |
| :--- | :---: | :--- | :--- |
| **1. Benny Edge Gateway** (`Benny.EdgeGateway`) | `8080` | **Front Door & Traffic Shield** | Authenticates callers (OAuth2/JWT) and enforces **atomic Redis Lua token-bucket rate limiting** at the perimeter via ASP.NET Core 10 Controllers / YARP so a noisy or abusive tenant cannot overload downstream services. |
| **2. Ingestion & Command Service** (`Benny.IngestionService`) | `8081` | **Frontline Command Coordinator** | Enforces **Idempotency-Key deduplication** (in `<1ms` via L1/L2 cache) so client retries never double-charge a customer, validates payloads, and orchestrates the synchronous `Risk Engine -> Ledger` fast path over **ASP.NET Core 10 Web API Controllers & HTTP/2**. |
| **3. Real-Time Risk Engine** (`Benny.RiskEngine`) | `8082` | **Instant Fraud & Rule Judge** | Blocks fraudulent or anomalous transactions *before* any money or state is committed. Uses **Structured Parallel Fan-Out (`Task.WhenAll` with a linked 5ms `CancellationTokenSource` deadline)** and **`AsyncLocal<RequestContext>`** to execute 3 checks in parallel: (1) Redis spending velocity, (2) `IMemoryCache` L1 cached tenant rules, and (3) heuristic anomaly scoring. |
| **4. Transaction Ledger Service** (`Benny.LedgerService`) | `8083` | **System of Record (Accounting)** | Authoritative owner of account balances and immutable ledger entries in **PostgreSQL 17** (`EF Core 10` / `Dapper` with optimistic concurrency). Uses the **Transactional Outbox Pattern** (`outbox_events` table) so the database commit and event creation happen in one atomic ACID transaction (eliminating dual-write bugs). |
| **5. Stream Processor Service** (`Benny.StreamProcessor`) | `8084` | **Big Data Pipeline Worker** | Consumes events from **Apache Kafka**, groups them into micro-batches using **`System.Threading.Channels` & C# 14 `.Chunk(100)`**, and fans out parallel bulk writes (`Task.WhenAll`) to **ScyllaDB** (time-series audit log), **ClickHouse** (real-time OLAP analytics), and **Iceberg/S3** (cold lakehouse), with non-blocking retry topics and **Dead Letter Queue (`benny.dlq`)** isolation. |
| **6. CQRS Query & Analytics Service** (`Benny.QueryService`) | `8085` | **Dashboard & Reporting API** | Dedicated **CQRS Read Plane** (Web API Controllers) serving live merchant summaries, fraud KPIs, and transaction history from ClickHouse, ScyllaDB, and Redis read-through cache so heavy analytical queries never compete with live transaction writes. |

---

## 3. Why Data Streaming Instead of Batch Processing?

A common architectural question is: *Why use real-time data streaming (**Apache Kafka + Stream Processor**) instead of traditional **Batch Processing** (running an hourly or nightly ETL/cron job)?*

### 3.1 The 2 Core Reasons We Need Streaming

1. **Protecting the `<15ms` Fast Write Path (Asynchronous Decoupling)**:
   - Every transaction needs to update 4 places: **PostgreSQL** (ACID ledger), **Redis** (live rolling velocity counters), **ScyllaDB** (audit/telemetry log), and **ClickHouse** (real-time analytics).
   - If the synchronous checkout API wrote to all 4 databases during the user's HTTP call, latency would balloon from **12ms to 150ms+**, and a brief slowdown in ClickHouse would cause live customer payments to fail.
   - By writing only to PostgreSQL (Ledger + Outbox) on the synchronous path and streaming the event through **Apache Kafka**, the user gets a response in `<15ms` while downstream stores are updated asynchronously within milliseconds.
2. **Acting as a Shock Absorber During 100x Traffic Spikes (Backpressure)**:
   - If traffic suddenly jumps from `5,000 req/s` to `100,000 req/s`, direct writes would overwhelm downstream analytical databases.
   - **Apache Kafka** buffers the burst safely on disk across replicated partitions while **KEDA** automatically scales out more `Benny.StreamProcessor` pods (`3 -> 64 pods`) based on Kafka consumer lag until the backlog is drained—**with zero dropped events**.

### 3.2 Real-Time Streaming vs. Traditional Batch Processing

Imagine a stolen card or compromised partner API key starts firing **500 fraudulent transactions per second**:

| Dimension | Traditional Batch Processing (Hourly / Nightly ETL) | Real-Time Data Streaming (Apache Kafka + Stream Processor) |
| :--- | :--- | :--- |
| **Fraud & Risk Prevention** | **Too Late**: Velocity counters and risk aggregations only update when the next batch runs (15 mins to 24 hours later). The attacker drains the account before the batch job even starts. | **Sub-Second (`<100ms`)**: Every event immediately updates rolling velocity windows in Redis & ClickHouse. Transaction #5 is automatically blocked by the Risk Engine. |
| **Database Load Pattern** | **Spiky "Batch Storms"**: Every hour or at midnight, a massive batch query scans millions of rows from PostgreSQL, spiking CPU/IO to 100% and degrading live user traffic. | **Smooth Continuous Flow**: Events stream continuously via the Outbox Relay (`FOR UPDATE SKIP LOCKED`), putting negligible load on PostgreSQL. |
| **Live Dashboards & Alerts** | **Stale Data**: Merchants and SREs looking at dashboards see metrics that are hours old. | **Real-Time Visibility**: ClickHouse and Grafana display live transaction volume, P99 risk scores, and anomaly rates within ~500ms. |
| **Failure Recovery** | **All-or-Nothing Reruns**: If a 2-hour batch job fails at 95%, teams must manually reconcile or re-run millions of records. | **Granular Replay & DLQ**: Kafka tracks exact partition offsets. If 1 poison-pill message fails, it routes to `benny.dlq` while the other 999,999 events process uninterrupted. |

### 3.3 Combining the Best of Both Worlds: Streaming + In-Stream Micro-Batching + Cold Batch Lakehouse

Writing 1 row at a time (`event-by-event`) into columnar/wide-column stores like **ClickHouse** or **ScyllaDB** is inefficient. **Project Benny** combines all three paradigms where each shines:
1. **Real-Time Event Streaming (`Apache Kafka`)**: Events flow continuously with millisecond latency.
2. **In-Stream Micro-Batching (`System.Threading.Channels` + `.Chunk(100)`)**: Inside `Benny.StreamProcessor`, incoming stream events are grouped into 100-event micro-batches (or 50ms time windows) and flushed in a single bulk insert to **ScyllaDB** and **ClickHouse**—delivering **real-time freshness (`<200ms`)** *plus* **batch-level write throughput**.
3. **Cold Lakehouse Batch Processing (`Apache Iceberg on S3/MinIO`)**: For workloads that genuinely belong in batch—such as **monthly regulatory reporting, historical backfills, and training AI/ML fraud models over 6 months of history**—events are compacted into **Apache Iceberg Parquet files**, allowing batch engines to scan petabytes cheaply without touching live operational databases.

> For a detailed engineering reference, see [docs/architecture/architecture-deep-dive.md](docs/architecture/architecture-deep-dive.md) and [docs/implementation_plan.md](docs/implementation_plan.md).

---

## 4. How Cloud-Native Requirements Are Mapped to Technology

* **High Availability (`99.99%+ SLA`)**:
  * Multi-AZ active-active stateless compute pods (`topologySpreadConstraints`, `podAntiAffinity`), graceful connection draining (`HostOptions.ShutdownTimeout = 30s`), PostgreSQL 17 synchronous standby (`RPO=0`), Redis 8 Cluster, and Kafka KRaft (`RF=3`, `min.insync.replicas=2`).
* **High Performance & Low Latency (`<15ms P99`)**:
  * Two-tier near-cache (**L1 `IMemoryCache` / `HybridCache`** `<100µs` + **L2 Redis 8** `<1ms`) with Redis Pub/Sub invalidation and single-flight stampede protection.
  * **ASP.NET Core 10 Web API Controllers on Kestrel**, **Non-Blocking `ValueTask`/`Task` I/O**, **Structured Parallel Fan-Out (`Task.WhenAll` + linked `CancellationTokenSource`)**, **`AsyncLocal<RequestContext>`**, and **.NET 10 Server GC + DATAS + Dynamic PGO (`TieredPGO`)**.
* **Durability & Resilience to Failures**:
  * **Transactional Outbox Pattern** (zero dual-write bugs), **Idempotency-Key locks**, **Polly v8 (`Microsoft.Extensions.Resilience`)** (ConcurrencyLimiter/Bulkhead, Timeout, CircuitBreaker, Exponential Backoff with Jitter), and **Kafka Dead Letter Queues (`benny.dlq`)**.
* **Elastic Horizontal Scaling & Massive Data**:
  * **Kubernetes HPA** (`3 -> 100+ pods` for API services) + **KEDA** (`3 -> 64 pods` for Kafka stream consumers).
  * **Polyglot Persistence**: **PostgreSQL 17** (ACID ledger) + **Redis 8** (distributed state) + **ScyllaDB** (high-write time-series) + **ClickHouse** (real-time OLAP) + **Apache Iceberg** (cold lakehouse).

---

## 5. System Architecture Diagrams (PlantUML)

All architecture diagrams are maintained as code in [`docs/architecture/`](docs/architecture/) and compiled to SVG and PNG formats:

### 5.1 Overall Cloud-Native System Architecture
- Source: [`docs/architecture/system-architecture.puml`](docs/architecture/system-architecture.puml)
- Rendered: [`docs/architecture/system-architecture.svg`](docs/architecture/system-architecture.svg) | [`docs/architecture/system-architecture.png`](docs/architecture/system-architecture.png)

![System Architecture](docs/architecture/system-architecture.svg)

### 5.2 Low-Latency Request & Durable Event Sequence Flow
- Source: [`docs/architecture/data-flow-sequence.puml`](docs/architecture/data-flow-sequence.puml)
- Rendered: [`docs/architecture/data-flow-sequence.svg`](docs/architecture/data-flow-sequence.svg) | [`docs/architecture/data-flow-sequence.png`](docs/architecture/data-flow-sequence.png)

![Data Flow Sequence](docs/architecture/data-flow-sequence.svg)

### 5.3 Elastic Horizontal Scaling, Polly v8 Resilience & High-Availability Topology
- Source: [`docs/architecture/scaling-resilience-topology.puml`](docs/architecture/scaling-resilience-topology.puml)
- Rendered: [`docs/architecture/scaling-resilience-topology.svg`](docs/architecture/scaling-resilience-topology.svg) | [`docs/architecture/scaling-resilience-topology.png`](docs/architecture/scaling-resilience-topology.png)

![Scaling & Resilience Topology](docs/architecture/scaling-resilience-topology.svg)

---

## 6. AI-Native Project Structure (`.agents/` & `AGENTS.md`)

This repository follows best practices for AI-assisted software engineering:

```text
benny-system/
├── AGENTS.md                                         # Root AI Agent Constitution & Architecture Rules
├── README.md                                         # Project Overview, Business Context & Architecture Guide
├── .agents/
│   ├── rules/
│   │   └── dotnet10-csharp14-standards.md            # .NET 10, C# 14 & ASP.NET Core 10 Controller Rules
│   ├── agents/                                       # Specialized AI Subagent Personas
│   │   ├── cloud-architect.md                        # Principal Cloud-Native System Architect
│   │   ├── dotnet-microservice-engineer.md           # Staff .NET 10 & C# 14 Microservice Engineer
│   │   ├── data-streaming-specialist.md              # Principal Cache, Kafka & Polyglot Data Engineer
│   │   └── sre-resilience-auditor.md                 # Principal SRE, Autoscaling & Chaos Engineer
│   ├── skills/                                       # On-Demand Procedural Skills (SKILL.md)
│   │   ├── scaffold-dotnet-service/SKILL.md          # Scaffolds .NET 10 + ASP.NET Core 10 services
│   │   ├── implement-caching-messaging/SKILL.md      # L1/L2 Near-Cache + Outbox + Kafka DLQ patterns
│   │   ├── resilience-and-scaling-audit/SKILL.md     # Polly v8 + KEDA/HPA + Server GC/PGO checklist
│   │   └── plantuml-architecture-sync/SKILL.md       # Updates & renders PlantUML diagrams
│   └── prompts/                                      # Reusable Project & Phase Prompts
│       ├── 00-master-project-prompt.md               # Master System Prompt for Project Benny
│       ├── 01-bootstrap-dotnet-monorepo.md           # Phase 1: .NET 10 Solution, CPM & Infra
│       ├── 02-implement-core-services.md             # Phase 2: Fast-Path Microservices
│       ├── 03-implement-data-and-streaming.md        # Phase 3: Kafka Streaming & Polyglot Data
│       └── 04-load-testing-and-chaos-engineering.md  # Phase 4: K8s HPA/KEDA & Chaos Testing
└── docs/
    ├── implementation_plan.md                        # Detailed 4-Phase Technical Implementation Plan
    └── architecture/                                 # Architecture Deep-Dive + PlantUML (.puml/.svg/.png)
        ├── architecture-deep-dive.md                 # Detailed Design Rationale (Streaming vs Batch, Polyglot DBs)
        ├── system-architecture.puml
        ├── data-flow-sequence.puml
        └── scaling-resilience-topology.puml
```
