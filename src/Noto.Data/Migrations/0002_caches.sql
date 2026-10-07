-- Local-only derived data; safe to delete and rebuild.
CREATE TABLE day_stats_cache (
    workspace_id TEXT NOT NULL,
    day TEXT NOT NULL,
    stats TEXT NOT NULL,
    PRIMARY KEY (workspace_id, day)
);

CREATE TABLE item_metrics_cache (
    item_id TEXT PRIMARY KEY,
    as_of TEXT NOT NULL,
    age INTEGER NOT NULL,
    carry INTEGER NOT NULL,
    defers INTEGER NOT NULL
);
