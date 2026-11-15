# Phase 4 Prompt — Resilience Audit, Kubernetes Autoscaling & Chaos Engineering

```markdown
Act as `sre-resilience-auditor` and activate the `resilience-and-scaling-audit` skill.

Task: Harden **Project Benny** for production cloud-native operations:
1. Audit all services for Polly v8 (`Microsoft.Extensions.Resilience`) CircuitBreaker, ConcurrencyLimiter (Bulkhead), Timeout, and Exponential Backoff with Jitter.
2. Create Kubernetes deployment manifests under `deploy/k8s/`:
   - Multi-AZ PodAntiAffinity, TopologySpreadConstraints, Readiness/Liveness/Startup probes, and graceful termination hooks.
   - `HorizontalPodAutoscaler` (HPA) for `benny-edge-gateway`, `benny-ingestion-service`, `benny-risk-engine`, and `benny-query-service`.
   - KEDA `ScaledObject` for `benny-stream-processor` driven by Apache Kafka consumer lag.
3. Create NBomber / k6 / bash load test suites in `tests/` simulating high-concurrency bursts and chaos tests verifying zero data loss during Redis or Kafka broker outages.
```
