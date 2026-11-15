---
name: implement-caching-messaging
description: >-
  Use this skill when implementing multi-tier caching (L1 IMemoryCache/HybridCache + L2 Redis)
  or durable event streaming (Apache Kafka + Transactional Outbox) in Benny services.
---

# Skill: Implement Multi-Tier Caching & Durable Event Messaging

Use this runbook to implement industry-standard caching and messaging patterns in the Benny System.

## Part A: Two-Tier Near-Cache (L1 IMemoryCache + L2 Redis Cluster)

1. **Read Flow (Cache-Aside with Near-Cache)**:
   - Query **L1 (`IMemoryCache`)** in-memory cache first (`<100µs`).
   - On L1 miss, query **L2 (`StackExchange.Redis` / Redis 8 / Valkey)** distributed cache (`<1ms`).
   - On L2 miss, use single-flight coalescing (`ConcurrentDictionary<string, Lazy<Task<T>>>` or `HybridCache`) to prevent **cache stampede**, load from the primary datastore, populate L2 with TTL + jitter, and populate L1.
2. **Invalidation Flow**:
   - On write/update, update or evict L2 Redis key and publish an invalidation message to the Redis Pub/Sub channel `benny:cache:invalidation` via `ISubscriber.PublishAsync`.
   - All horizontally scaled pods subscribe to `benny:cache:invalidation` in a `BackgroundService` and immediately evict the stale key from their local L1 `IMemoryCache` instance.
3. **Idempotency Barrier**:
   - Use atomic Redis Lua script (`SET key IN_PROGRESS NX EX 60`) via `IDatabase.ScriptEvaluateAsync` at the ingestion edge to deduplicate concurrent retries before executing business logic.

## Part B: Durable Kafka Messaging & Transactional Outbox

1. **Zero Dual-Write Guarantee (Transactional Outbox)**:
   - Never publish directly to Kafka inside a database transaction method.
   - Persist both the domain entity mutation and an `OutboxEventRecord` (UUID v7 `Guid.CreateVersion7()`, aggregateType, aggregateId, eventType, JSON/Avro payload, traceHeaders, createdAt) in the **same PostgreSQL ACID transaction** (`NpgsqlTransaction`).
   - Stream `outbox_events` to Apache Kafka via **Debezium CDC** or a high-efficiency **SKIP LOCKED Outbox Relay** `BackgroundService` polling worker.
2. **High-Throughput Kafka Producer Settings (`Confluent.Kafka` `ProducerConfig`)**:
   - `Acks = Acks.All`
   - `EnableIdempotence = true`
   - `CompressionType = CompressionType.Lz4` (or `Zstd`)
   - `LingerMs = 5`
   - `BatchSize = 65536`
3. **Resilient Consumer Group & Dead Letter Queue (DLQ)**:
   - Configure non-blocking retry topics or Polly exponential backoff (`1s -> 5s -> 30s`).
   - Route poison-pill messages that fail schema validation or exhaust retries to `<topic>.dlq` with original exception stacktrace and OpenTelemetry `traceparent` header preserved.
