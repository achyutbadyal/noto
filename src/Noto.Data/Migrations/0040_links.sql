-- Synced: the URL is user data. Id is UUIDv5(item, url) so two devices adding the same link merge.
CREATE TABLE todo_link (
    id TEXT PRIMARY KEY,
    item_id TEXT NOT NULL REFERENCES todo_item(id),
    url TEXT NOT NULL,
    position INTEGER NOT NULL,
    source TEXT NOT NULL,
    created_at TEXT NOT NULL,
    deleted_at TEXT
);
CREATE INDEX ix_link_item ON todo_link(item_id);

-- Local-only: previews hold private third-party content fetched with this device's credentials.
CREATE TABLE link_preview_cache (
    url TEXT PRIMARY KEY,
    provider_id TEXT NOT NULL,
    connection_id TEXT,
    fields TEXT NOT NULL,
    state_hash TEXT,
    preview_status TEXT NOT NULL,
    fetched_at TEXT NOT NULL,
    expires_at TEXT,
    user_last_viewed_at TEXT,
    viewed_state_hash TEXT
);
CREATE INDEX ix_preview_connection ON link_preview_cache(connection_id);

-- Local-only metadata. Secrets are never stored here (OS keyring).
CREATE TABLE app_connection (
    id TEXT PRIMARY KEY,
    provider_id TEXT NOT NULL,
    auth_method TEXT NOT NULL,
    display_label TEXT NOT NULL,
    instance_url TEXT,
    scopes TEXT NOT NULL,
    status TEXT NOT NULL,
    connected_at TEXT NOT NULL,
    last_used_at TEXT
);
