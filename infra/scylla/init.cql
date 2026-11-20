CREATE KEYSPACE IF NOT EXISTS benny_telemetry
WITH replication = {'class': 'SimpleStrategy', 'replication_factor': 1};

CREATE TABLE IF NOT EXISTS benny_telemetry.events_by_tenant_day (
    tenant_id  text,
    bucket_day text,
    event_time timestamp,
    tx_id      uuid,
    account_id text,
    amount     double,
    currency   text,
    risk_score double,
    status     text,
    PRIMARY KEY ((tenant_id, bucket_day), event_time, tx_id)
) WITH CLUSTERING ORDER BY (event_time DESC, tx_id ASC);
