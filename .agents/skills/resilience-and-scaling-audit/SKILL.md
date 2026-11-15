---
name: resilience-and-scaling-audit
description: >-
  Use this skill when auditing or configuring fault tolerance (Polly v8 / Microsoft.Extensions.Resilience),
  horizontal autoscaling (Kubernetes HPA / KEDA), and high-load performance tuning.
---

# Skill: Cloud-Native Resilience & Horizontal Scaling Audit

Use this checklist and configuration guide to verify that any Benny microservice is production-ready for extreme traffic spikes and partial infrastructure failures.

## Step 1: Verify Polly v8 (`ResiliencePipeline`) Protection Order
Ensure external calls apply `Microsoft.Extensions.Resilience` / `Microsoft.Extensions.Http.Resilience` strategies in the standard outer-to-inner pipeline order:
`TotalRequestTimeout -> Retry (with Jitter) -> CircuitBreaker -> ConcurrencyLimiter (Bulkhead) -> AttemptTimeout`

1. **Circuit Breaker (`CircuitBreakerStrategyOptions`)**:
   - `SamplingDuration = TimeSpan.FromSeconds(10)`
   - `MinimumThroughput = 100`
   - `FailureRatio = 0.5`
   - `BreakDuration = TimeSpan.FromSeconds(10)`
2. **Concurrency Limiter / Bulkhead (`ConcurrencyLimiterOptions`)**:
   - Use semaphore-based `AddConcurrencyLimiter(permitLimit, queueLimit)` to cap max concurrent async operations calling each dependency and prevent connection pool exhaustion.
3. **Retry with Jitter (`RetryStrategyOptions`)**:
   - Only retry idempotent operations and transient network/timeout errors.
   - Set `BackoffType = DelayBackoffType.Exponential` and `UseJitter = true` to prevent retry storms across scaled pods.

## Step 2: Verify Horizontal Pod Autoscaling (HPA & KEDA)
1. **Stateless API Services (`Benny.EdgeGateway`, `Benny.IngestionService`, `Benny.RiskEngine`, `Benny.QueryService`)**:
   - Ensure zero local state affinity.
   - Configure Kubernetes `HorizontalPodAutoscaler` scaling on CPU utilization (70%) and custom Prometheus metric `http_server_request_duration_seconds_count`.
2. **Event Consumer Services (`Benny.StreamProcessor`)**:
   - Configure a KEDA `ScaledObject` targeting the Apache Kafka consumer group lag (`lagThreshold: "5000"` per partition), scaling from `minReplicaCount: 3` up to `maxReplicaCount: <topic_partition_count>`.

## Step 3: Verify .NET 10 Runtime & Connection Pool Sizing for Scale
- **.NET Runtime GC & JIT**: `DOTNET_gcServer=1`, `DOTNET_GCDynamicAdaptationMode=1` (DATAS), `DOTNET_TieredPGO=1`.
- **Database Pool (`NpgsqlDataSource`)**: Keep `MaxPoolSize=25` bounded per pod even when thousands of async requests are active, preventing PostgreSQL connection exhaustion during pod scale-out (or route through PgBouncer).
