# Phase 3 Prompt — Implement High-Volume Event Streaming & Polyglot Data Plane

```markdown
Act as `data-streaming-specialist` and activate the `implement-caching-messaging` skill.

Task: Implement the asynchronous event streaming and large-scale data handling components:
1. Outbox CDC / Relay `BackgroundService` worker publishing validated ledger events to Apache Kafka (`benny.transactions.validated`) with source-generated JSON/Protobuf serialization and `Acks.All`.
2. `src/Services/Benny.StreamProcessor`:
   - High-throughput Kafka consumer group using `System.Threading.Channels` and `.Chunk(100)` for micro-batching.
   - Parallel sink writers (`Task.WhenAll`) persisting raw event logs into **ScyllaDB**, real-time analytical rollups into **ClickHouse**, and rolling counters into **Redis**.
   - Non-blocking retry topics and Dead Letter Queue (`benny.dlq`) handler for poison pills.
3. `src/Services/Benny.QueryService`:
   - CQRS Web API Controller (`[ApiController]`) read endpoints querying ClickHouse for real-time analytical aggregations and ScyllaDB/PostgreSQL for point-in-time lookups, fronted by the Two-Tier Near-Cache.
```
