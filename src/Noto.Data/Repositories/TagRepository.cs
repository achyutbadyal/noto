using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Sync;

namespace Noto.Data.Repositories;

sealed class TagRepository(SqliteConnection conn, SqliteTransaction? tx, ChangeRecorder? rec = null)
    : ITagRepository
{
    public async Task UpsertAsync(Tag tag)
    {
        var before = rec?.Active == true ? await rec.LoadAsync(EntityTypes.Tag, tag.Id) : null;
        using var cmd = Sql.Cmd(
            conn,
            tx,
            """
            INSERT INTO tag (id, workspace_id, name, color) VALUES ($id, $ws, $name, $color)
            ON CONFLICT(id) DO UPDATE SET name=$name, color=$color
            """,
            ("$id", tag.Id.ToString()),
            ("$ws", tag.WorkspaceId.ToString()),
            ("$name", tag.Name),
            ("$color", tag.Color)
        );
        await cmd.ExecuteNonQueryAsync();
        if (rec?.Active == true)
            await rec.RecordRowStoreAsync(EntityTypes.Tag, tag.Id, before);
    }

    public async Task<IReadOnlyList<Tag>> ListAsync(Guid workspaceId)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "SELECT * FROM tag WHERE workspace_id = $ws ORDER BY name",
            ("$ws", workspaceId.ToString())
        );
        using var r = await cmd.ExecuteReaderAsync();
        var tags = new List<Tag>();
        while (await r.ReadAsync())
            tags.Add(
                new Tag
                {
                    Id = r.Guid("id")!.Value,
                    WorkspaceId = r.Guid("workspace_id")!.Value,
                    Name = r.Str("name")!,
                    Color = r.Str("color")!,
                }
            );
        return tags;
    }

    public async Task SetItemTagsAsync(Guid itemId, IReadOnlyCollection<Guid> tagIds)
    {
        var previous = rec?.Active == true ? await GetItemTagIdsAsync(itemId) : null;
        using (
            var del = Sql.Cmd(
                conn,
                tx,
                "DELETE FROM todo_tag WHERE item_id = $id",
                ("$id", itemId.ToString())
            )
        )
            await del.ExecuteNonQueryAsync();
        foreach (var tag in tagIds.Distinct())
        {
            using var ins = Sql.Cmd(
                conn,
                tx,
                "INSERT INTO todo_tag (item_id, tag_id) VALUES ($i, $t)",
                ("$i", itemId.ToString()),
                ("$t", tag.ToString())
            );
            await ins.ExecuteNonQueryAsync();
        }
        if (previous is not null)
            await rec!.RecordItemTagsAsync(itemId, previous, tagIds.Distinct().ToList());
    }

    public async Task<IReadOnlyList<Guid>> GetItemTagIdsAsync(Guid itemId)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "SELECT tag_id FROM todo_tag WHERE item_id = $id",
            ("$id", itemId.ToString())
        );
        using var r = await cmd.ExecuteReaderAsync();
        var ids = new List<Guid>();
        while (await r.ReadAsync())
            ids.Add(Guid.Parse(r.GetString(0)));
        return ids;
    }

    public async Task<IReadOnlyList<(Guid ItemId, Guid TagId)>> ListItemTagsAsync(Guid workspaceId)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "SELECT tt.item_id, tt.tag_id FROM todo_tag tt JOIN todo_item i ON i.id = tt.item_id WHERE i.workspace_id = $ws",
            ("$ws", workspaceId.ToString())
        );
        using var r = await cmd.ExecuteReaderAsync();
        var pairs = new List<(Guid, Guid)>();
        while (await r.ReadAsync())
            pairs.Add((Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1))));
        return pairs;
    }
}
