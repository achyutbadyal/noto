CREATE TABLE workspace (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    icon TEXT NOT NULL,
    color TEXT NOT NULL,
    time_zone TEXT NOT NULL,
    tz_follows_device INTEGER NOT NULL,
    day_boundary TEXT NOT NULL,
    focus_hours TEXT,
    capacity_unit TEXT NOT NULL,
    daily_capacity INTEGER NOT NULL,
    preset TEXT NOT NULL,
    layout TEXT NOT NULL,
    sort_order_mode TEXT NOT NULL,
    pressure TEXT NOT NULL,
    pressure_overrides TEXT,
    layout_settings TEXT,
    now_item_id TEXT,
    sync_enabled INTEGER NOT NULL,
    sort_rank INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    archived_at TEXT,
    deleted_at TEXT
);

CREATE TABLE todo_item (
    id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspace(id),
    parent_id TEXT REFERENCES todo_item(id),
    is_container INTEGER NOT NULL,
    title TEXT NOT NULL,
    notes TEXT,
    status TEXT NOT NULL,
    is_someday INTEGER NOT NULL,
    planned_for TEXT,
    due_date TEXT,
    estimate_minutes INTEGER,
    priority INTEGER NOT NULL,
    waiting_on TEXT,
    drop_reason TEXT,
    time_of_day TEXT,
    board_column TEXT,
    manual_rank TEXT NOT NULL,
    recurrence_rule_id TEXT,
    occurrence_date TEXT,
    completed_on TEXT,
    created_at TEXT NOT NULL,
    created_tz TEXT NOT NULL,
    completed_at TEXT,
    dropped_at TEXT,
    deleted_at TEXT
);
CREATE INDEX ix_item_workspace_status ON todo_item(workspace_id, status, planned_for);

CREATE TABLE item_event (
    id TEXT PRIMARY KEY,
    item_id TEXT NOT NULL REFERENCES todo_item(id),
    workspace_id TEXT NOT NULL,
    type TEXT NOT NULL,
    data TEXT,
    occurred_at TEXT NOT NULL,
    tz TEXT NOT NULL,
    device_id TEXT NOT NULL,
    hlc TEXT NOT NULL
);
CREATE INDEX ix_event_item ON item_event(item_id, occurred_at);
