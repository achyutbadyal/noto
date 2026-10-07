CREATE TABLE recurrence_rule (
    id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspace(id),
    rrule TEXT NOT NULL,
    template TEXT NOT NULL,
    missed_behavior TEXT NOT NULL,
    target_count INTEGER,
    target_period TEXT,
    start_date TEXT NOT NULL,
    end_date TEXT,
    deleted_at TEXT
);

CREATE TABLE tag (
    id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspace(id),
    name TEXT NOT NULL,
    color TEXT NOT NULL
);

CREATE TABLE todo_tag (
    item_id TEXT NOT NULL REFERENCES todo_item(id),
    tag_id TEXT NOT NULL REFERENCES tag(id),
    PRIMARY KEY (item_id, tag_id)
);

CREATE TABLE day_note (
    id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspace(id),
    day TEXT NOT NULL,
    kind TEXT NOT NULL,
    text TEXT NOT NULL,
    UNIQUE (workspace_id, day, kind)
);
