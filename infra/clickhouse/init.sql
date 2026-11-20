CREATE DATABASE IF NOT EXISTS benny_analytics;

CREATE TABLE IF NOT EXISTS benny_analytics.transaction_events
(
    event_id       UUID,
    transaction_id UUID,
    tenant_id      String,
    account_id     String,
    amount         Decimal(18, 4),
    currency       LowCardinality(String),
    merchant_id    String,
    risk_score     Decimal(5, 4),
    status         LowCardinality(String),
    occurred_at    DateTime64(3, 'UTC')
)
ENGINE = MergeTree()
PARTITION BY toYYYYMM(occurred_at)
ORDER BY (tenant_id, occurred_at, transaction_id);
