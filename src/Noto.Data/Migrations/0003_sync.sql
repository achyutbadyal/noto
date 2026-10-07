-- Local-only sync bookkeeping; never itself synced.
CREATE TABLE pending_ops (
    seq INTEGER PRIMARY KEY AUTOINCREMENT,
    op_id TEXT NOT NULL UNIQUE,
    workspace_id TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    field TEXT,
    op TEXT NOT NULL
);
CREATE INDEX ix_pending_field ON pending_ops(entity_type, entity_id, field);

CREATE TABLE field_clocks (
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    field TEXT NOT NULL,
    hlc TEXT NOT NULL,
    PRIMARY KEY (entity_type, entity_id, field)
);

CREATE TABLE sync_state (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE conflict_log (
    id TEXT PRIMARY KEY,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    field TEXT NOT NULL,
    losing_value TEXT,
    losing_hlc TEXT NOT NULL,
    recorded_at TEXT NOT NULL
);
CREATE INDEX ix_conflict_entity ON conflict_log(entity_id);

-- Raw per-field LWW result. The typed row is normalize(raw), so merge order can never lose a field's value.
CREATE TABLE sync_raw (
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    row TEXT NOT NULL,
    PRIMARY KEY (entity_type, entity_id)
);
