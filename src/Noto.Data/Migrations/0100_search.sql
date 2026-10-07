-- Full-text index over item title/notes. Triggers keep it in step with every write, including synced ones.
CREATE VIRTUAL TABLE item_fts USING fts5(title, notes, content='todo_item', content_rowid='rowid');

CREATE TRIGGER todo_item_fts_ai AFTER INSERT ON todo_item BEGIN
    INSERT INTO item_fts(rowid, title, notes) VALUES (new.rowid, new.title, new.notes);
END;

CREATE TRIGGER todo_item_fts_ad AFTER DELETE ON todo_item BEGIN
    INSERT INTO item_fts(item_fts, rowid, title, notes) VALUES ('delete', old.rowid, old.title, old.notes);
END;

CREATE TRIGGER todo_item_fts_au AFTER UPDATE OF title, notes ON todo_item BEGIN
    INSERT INTO item_fts(item_fts, rowid, title, notes) VALUES ('delete', old.rowid, old.title, old.notes);
    INSERT INTO item_fts(rowid, title, notes) VALUES (new.rowid, new.title, new.notes);
END;

INSERT INTO item_fts(item_fts) VALUES ('rebuild');
