using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Recurrence;
using Noto.Core.Sync;

namespace Noto.Data.Repositories;

// Plain SQL <-> JSON rows for the synced entity types without a typed mapper. Column names are the sync
// field names, and text columns already hold ISO dates/instants, so values pass through unchanged.
sealed class SyncRowStore(SqliteConnection conn, SqliteTransaction? tx) : ISyncRowStore
{
    enum Kind
    {
        Text,
        NullableText,
        Int,
        NullableInt,
    }

    sealed record Table(string Name, (string Column, Kind Kind)[] Columns, string? WorkspaceFilter);

    static readonly Dictionary<string, Table> Tables = new()
    {
        [EntityTypes.RecurrenceRule] = new(
            "recurrence_rule",
            [
                ("workspace_id", Kind.Text),
                ("rrule", Kind.Text),
                ("template", Kind.Text),
                ("missed_behavior", Kind.Text),
                ("target_count", Kind.NullableInt),
                ("target_period", Kind.NullableText),
                ("start_date", Kind.Text),
                ("end_date", Kind.NullableText),
                ("deleted_at", Kind.NullableText),
            ],
            "workspace_id = $ws"
        ),
        [EntityTypes.Tag] = new(
            "tag",
            [("workspace_id", Kind.Text), ("name", Kind.Text), ("color", Kind.Text)],
            "workspace_id = $ws"
        ),
        [EntityTypes.DayNote] = new(
            "day_note",
            [
                ("workspace_id", Kind.Text),
                ("day", Kind.Text),
                ("kind", Kind.Text),
                ("text", Kind.Text),
            ],
            "workspace_id = $ws"
        ),
        [EntityTypes.TodoLink] = new(
            "todo_link",
            [
                ("item_id", Kind.Text),
                ("url", Kind.Text),
                ("position", Kind.Int),
                ("source", Kind.Text),
                ("created_at", Kind.Text),
                ("deleted_at", Kind.NullableText),
            ],
            "item_id IN (SELECT id FROM todo_item WHERE workspace_id = $ws)"
        ),
    };

    public async Task<JsonObject?> LoadAsync(string entityType, Guid id)
    {
        if (entityType == EntityTypes.TodoTag)
            return null; // keyed by (item, tag); the raw row is authoritative
        var t = Tables[entityType];
        using var cmd = Sql.Cmd(
            conn,
            tx,
            $"SELECT id, {Cols(t)} FROM {t.Name} WHERE id = $id",
            ("$id", id.ToString())
        );
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Read(r, t) : null;
    }

    public async Task SaveAsync(string entityType, JsonObject row)
    {
        if (entityType == EntityTypes.TodoTag)
        {
            await SaveTodoTagAsync(row);
            return;
        }

        var t = Tables[entityType];
        var args = new List<(string, object)> { ("$id", row["id"]!.GetValue<string>()) };
        foreach (var (column, kind) in t.Columns)
            args.Add(($"${column}", Value(row[column], kind)));

        var names = string.Join(", ", t.Columns.Select(c => c.Column));
        var values = string.Join(", ", t.Columns.Select(c => "$" + c.Column));
        var updates = string.Join(", ", t.Columns.Select(c => $"{c.Column} = ${c.Column}"));
        using var cmd = Sql.Cmd(
            conn,
            tx,
            $"INSERT INTO {t.Name} (id, {names}) VALUES ($id, {values}) ON CONFLICT(id) DO UPDATE SET {updates}",
            args.ToArray()
        );
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<JsonObject>> ListAsync(string entityType, Guid workspaceId)
    {
        if (entityType == EntityTypes.TodoTag)
            return await ListTodoTagsAsync(workspaceId);
        var t = Tables[entityType];
        using var cmd = Sql.Cmd(
            conn,
            tx,
            $"SELECT id, {Cols(t)} FROM {t.Name} WHERE {t.WorkspaceFilter}",
            ("$ws", workspaceId.ToString())
        );
        using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<JsonObject>();
        while (await r.ReadAsync())
            rows.Add(Read(r, t));
        return rows;
    }

    // Present rows plus tombstones kept in the raw shadow, so a removed tag still syncs as removed.
    async Task<IReadOnlyList<JsonObject>> ListTodoTagsAsync(Guid workspaceId)
    {
        var rows = new Dictionary<string, JsonObject>();
        using (
            var raw = Sql.Cmd(
                conn,
                tx,
                """
                SELECT r.row FROM sync_raw r JOIN todo_item i ON i.id = json_extract(r.row, '$.item_id')
                WHERE r.entity_type = 'todo_tag' AND i.workspace_id = $ws
                """,
                ("$ws", workspaceId.ToString())
            )
        )
        using (var rr = await raw.ExecuteReaderAsync())
            while (await rr.ReadAsync())
            {
                var row = JsonNode.Parse(rr.GetString(0))!.AsObject();
                rows[row["id"]!.GetValue<string>()] = row;
            }

        using var present = Sql.Cmd(
            conn,
            tx,
            "SELECT t.item_id, t.tag_id FROM todo_tag t JOIN todo_item i ON i.id = t.item_id WHERE i.workspace_id = $ws",
            ("$ws", workspaceId.ToString())
        );
        using var pr = await present.ExecuteReaderAsync();
        while (await pr.ReadAsync())
        {
            var (item, tag) = (Guid.Parse(pr.GetString(0)), Guid.Parse(pr.GetString(1)));
            var id = TodoTagId(item, tag).ToString();
            rows[id] = new JsonObject
            {
                ["id"] = id,
                ["item_id"] = item.ToString(),
                ["tag_id"] = tag.ToString(),
                ["removed_at"] = null,
            };
        }
        return rows.Values.ToList();
    }

    async Task SaveTodoTagAsync(JsonObject row)
    {
        var item = row["item_id"]?.GetValue<string>();
        var tag = row["tag_id"]?.GetValue<string>();
        if (item is null || tag is null)
            return; // partial row; completed by a later op in the same merge

        var sql = row["removed_at"] is null
            ? "INSERT OR IGNORE INTO todo_tag (item_id, tag_id) VALUES ($i, $t)"
            : "DELETE FROM todo_tag WHERE item_id = $i AND tag_id = $t";
        using var cmd = Sql.Cmd(conn, tx, sql, ("$i", item), ("$t", tag));
        await cmd.ExecuteNonQueryAsync();
    }

    public static Guid TodoTagId(Guid itemId, Guid tagId) => Uuid5.Create(itemId, tagId.ToString());

    static string Cols(Table t) => string.Join(", ", t.Columns.Select(c => c.Column));

    static JsonObject Read(SqliteDataReader r, Table t)
    {
        var row = new JsonObject { ["id"] = r.GetString(0) };
        for (var i = 0; i < t.Columns.Length; i++)
        {
            var (column, kind) = t.Columns[i];
            row[column] =
                r.IsDBNull(i + 1) ? null
                : kind is Kind.Int or Kind.NullableInt ? r.GetInt32(i + 1)
                : r.GetString(i + 1);
        }
        return row;
    }

    // Partial rows fall back to column defaults so NOT NULL columns never block a merge.
    static object Value(JsonNode? node, Kind kind) =>
        kind switch
        {
            Kind.Text => node?.GetValue<string>() ?? "",
            Kind.Int => node?.GetValue<int>() ?? 0,
            Kind.NullableInt => node is null ? DBNull.Value : node.GetValue<int>(),
            _ => node is null ? DBNull.Value : node.GetValue<string>(),
        };
}
