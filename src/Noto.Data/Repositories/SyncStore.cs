using System.Text.Json;
using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Sync;

namespace Noto.Data.Repositories;

sealed class SyncStore(SqliteConnection conn, SqliteTransaction tx) : ISyncStore
{
    ChangeRecorder? _recorder;

    public void Attach(ChangeRecorder recorder) => _recorder = recorder;

    public IDisposable SuppressRecording() => _recorder?.Suppress() ?? NoopDisposable.Instance;

    sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }

    public async Task AddPendingAsync(Op op)
    {
        using var cmd = Sql.Cmd(conn, tx,
            "INSERT OR IGNORE INTO pending_ops (op_id, workspace_id, entity_type, entity_id, field, op) VALUES ($id, $ws, $type, $entity, $field, $op)",
            ("$id", op.OpId.ToString()), ("$ws", op.WorkspaceId.ToString()), ("$type", op.EntityType),
            ("$entity", op.EntityId.ToString()), ("$field", Sql.Val(op.Field)),
            ("$op", JsonSerializer.Serialize(op, SyncJson.Default.Op)));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<Op>> ListPendingAsync(int limit)
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT op FROM pending_ops ORDER BY seq LIMIT $limit", ("$limit", limit));
        using var r = await cmd.ExecuteReaderAsync();
        var ops = new List<Op>();
        while (await r.ReadAsync()) ops.Add(JsonSerializer.Deserialize(r.GetString(0), SyncJson.Default.Op)!);
        return ops;
    }

    public async Task RemovePendingAsync(IReadOnlyCollection<Guid> opIds)
    {
        foreach (var id in opIds)
        {
            using var cmd = Sql.Cmd(conn, tx, "DELETE FROM pending_ops WHERE op_id = $id", ("$id", id.ToString()));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task ClearPendingAsync(Guid workspaceId)
    {
        using var cmd = Sql.Cmd(conn, tx, "DELETE FROM pending_ops WHERE workspace_id = $ws", ("$ws", workspaceId.ToString()));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> HasPendingForFieldAsync(string entityType, Guid entityId, string field)
    {
        using var cmd = Sql.Cmd(conn, tx,
            "SELECT 1 FROM pending_ops WHERE entity_type = $t AND entity_id = $id AND field = $f LIMIT 1",
            ("$t", entityType), ("$id", entityId.ToString()), ("$f", field));
        return await cmd.ExecuteScalarAsync() is not null;
    }

    public async Task<Dictionary<string, string>> GetClocksAsync(string entityType, Guid entityId)
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT field, hlc FROM field_clocks WHERE entity_type = $t AND entity_id = $id",
            ("$t", entityType), ("$id", entityId.ToString()));
        using var r = await cmd.ExecuteReaderAsync();
        var clocks = new Dictionary<string, string>();
        while (await r.ReadAsync()) clocks[r.GetString(0)] = r.GetString(1);
        return clocks;
    }

    public async Task SetClockAsync(string entityType, Guid entityId, string field, string hlc)
    {
        using var cmd = Sql.Cmd(conn, tx,
            "INSERT OR REPLACE INTO field_clocks (entity_type, entity_id, field, hlc) VALUES ($t, $id, $f, $h)",
            ("$t", entityType), ("$id", entityId.ToString()), ("$f", field), ("$h", hlc));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<System.Text.Json.Nodes.JsonObject?> GetRawAsync(string entityType, Guid entityId)
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT row FROM sync_raw WHERE entity_type = $t AND entity_id = $id",
            ("$t", entityType), ("$id", entityId.ToString()));
        return await cmd.ExecuteScalarAsync() is string json ? System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject() : null;
    }

    public async Task SetRawAsync(string entityType, Guid entityId, System.Text.Json.Nodes.JsonObject row)
    {
        using var cmd = Sql.Cmd(conn, tx, "INSERT OR REPLACE INTO sync_raw (entity_type, entity_id, row) VALUES ($t, $id, $row)",
            ("$t", entityType), ("$id", entityId.ToString()), ("$row", row.ToJsonString()));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetStateAsync(string key)
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT value FROM sync_state WHERE key = $k", ("$k", key));
        return await cmd.ExecuteScalarAsync() as string;
    }

    public async Task SetStateAsync(string key, string value)
    {
        using var cmd = Sql.Cmd(conn, tx, "INSERT OR REPLACE INTO sync_state (key, value) VALUES ($k, $v)", ("$k", key), ("$v", value));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddConflictAsync(SyncConflict c)
    {
        using var cmd = Sql.Cmd(conn, tx,
            "INSERT OR IGNORE INTO conflict_log (id, entity_type, entity_id, field, losing_value, losing_hlc, recorded_at) VALUES ($id, $t, $e, $f, $v, $h, $at)",
            ("$id", c.Id.ToString()), ("$t", c.EntityType), ("$e", c.EntityId.ToString()), ("$f", c.Field),
            ("$v", Sql.Val(c.LosingValue)), ("$h", c.LosingHlc), ("$at", Sql.Val(c.RecordedAt)));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<SyncConflict>> ListConflictsAsync(Guid entityId)
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT * FROM conflict_log WHERE entity_id = $id ORDER BY recorded_at, rowid", ("$id", entityId.ToString()));
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<SyncConflict>();
        while (await r.ReadAsync())
            list.Add(new SyncConflict(r.Guid("id")!.Value, r.Str("entity_type")!, r.Guid("entity_id")!.Value, r.Str("field")!,
                r.Str("losing_value"), r.Str("losing_hlc")!, r.Instant("recorded_at")!.Value));
        return list;
    }

    public async Task DismissConflictsAsync(Guid entityId)
    {
        using var cmd = Sql.Cmd(conn, tx, "DELETE FROM conflict_log WHERE entity_id = $id", ("$id", entityId.ToString()));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeferForeignKeysAsync()
    {
        using var cmd = Sql.Cmd(conn, tx, "PRAGMA defer_foreign_keys = ON");
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> PurgeTombstonedItemsAsync(Guid workspaceId, DateTimeOffset before)
    {
        var ws = ("$ws", (object)workspaceId.ToString());
        var cutoff = ("$before", Sql.Val(before));
        const string doomed = "SELECT id FROM todo_item WHERE workspace_id = $ws AND deleted_at IS NOT NULL AND deleted_at < $before";

        using (var events = Sql.Cmd(conn, tx, $"DELETE FROM item_event WHERE item_id IN ({doomed})", ws, cutoff))
            await events.ExecuteNonQueryAsync();
        using (var raw = Sql.Cmd(conn, tx, $"DELETE FROM sync_raw WHERE entity_type = 'todo_item' AND entity_id IN ({doomed})", ws, cutoff))
            await raw.ExecuteNonQueryAsync();
        using (var clocks = Sql.Cmd(conn, tx, $"DELETE FROM field_clocks WHERE entity_type = 'todo_item' AND entity_id IN ({doomed})", ws, cutoff))
            await clocks.ExecuteNonQueryAsync();
        using var items = Sql.Cmd(conn, tx, "DELETE FROM todo_item WHERE workspace_id = $ws AND deleted_at IS NOT NULL AND deleted_at < $before", ws, cutoff);
        return await items.ExecuteNonQueryAsync();
    }
}
