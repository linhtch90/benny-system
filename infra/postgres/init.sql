CREATE TABLE IF NOT EXISTS account_balances (
    tenant_id   VARCHAR(128) NOT NULL,
    account_id  VARCHAR(128) NOT NULL,
    balance     NUMERIC(18, 4) NOT NULL DEFAULT 0,
    currency    VARCHAR(8) NOT NULL,
    version     BIGINT NOT NULL DEFAULT 1,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (tenant_id, account_id)
);

CREATE TABLE IF NOT EXISTS ledger_entries (
    entry_id       UUID PRIMARY KEY,
    transaction_id UUID NOT NULL,
    tenant_id      VARCHAR(128) NOT NULL,
    account_id     VARCHAR(128) NOT NULL,
    amount         NUMERIC(18, 4) NOT NULL,
    currency       VARCHAR(8) NOT NULL,
    balance_after  NUMERIC(18, 4) NOT NULL,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_ledger_entries_tx_id ON ledger_entries (transaction_id);
CREATE INDEX IF NOT EXISTS idx_ledger_entries_tenant_acc ON ledger_entries (tenant_id, account_id, created_at DESC);

CREATE TABLE IF NOT EXISTS outbox_events (
    id             UUID PRIMARY KEY,
    aggregate_type VARCHAR(128) NOT NULL,
    aggregate_id   VARCHAR(128) NOT NULL,
    event_type     VARCHAR(128) NOT NULL,
    topic          VARCHAR(256) NOT NULL,
    payload        JSONB NOT NULL,
    trace_parent   VARCHAR(256),
    created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    published_at   TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_outbox_unpublished ON outbox_events (created_at ASC) WHERE published_at IS NULL;
