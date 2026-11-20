#!/usr/bin/env bash
set -euo pipefail

GATEWAY_URL="${GATEWAY_URL:-http://localhost:8080}"
TENANT_ID="${TENANT_ID:-tenant-load-01}"
CONCURRENCY="${CONCURRENCY:-20}"
TOTAL_REQUESTS="${TOTAL_REQUESTS:-200}"

echo "=== Benny Load & Idempotency Smoke Test ==="
echo "Target: $GATEWAY_URL | Tenant: $TENANT_ID | Requests: $TOTAL_REQUESTS | Concurrency: $CONCURRENCY"

IDEM_KEY="idem-smoke-$(date +%s)"
PAYLOAD='{"accountId":"acc-001","amount":125.50,"currency":"USD","merchantId":"merch-99","countryCode":"US","deviceFingerprint":"fp-123"}'

echo "[1/2] Verifying Idempotency-Key deduplication..."
STATUS_1=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$GATEWAY_URL/api/v1/transactions" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -H "Idempotency-Key: $IDEM_KEY" \
  -d "$PAYLOAD")
STATUS_2=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$GATEWAY_URL/api/v1/transactions" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -H "Idempotency-Key: $IDEM_KEY" \
  -d "$PAYLOAD")
echo "First call HTTP: $STATUS_1 | Replay call HTTP: $STATUS_2"

echo "[2/2] Running concurrent burst ($TOTAL_REQUESTS requests)..."
seq 1 "$TOTAL_REQUESTS" | xargs -P "$CONCURRENCY" -I{} curl -s -o /dev/null -w "%{http_code}\n" \
  -X POST "$GATEWAY_URL/api/v1/transactions" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -H "Idempotency-Key: burst-{}-$(date +%s%N)" \
  -d "$PAYLOAD" | sort | uniq -c

echo "=== Done ==="
