using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Links;
using Noto.Core.Recurrence;
using Noto.Core.Sync;

namespace Noto.Data.Repositories;

sealed class LinkRepository(SqliteConnection conn, SqliteTransaction? tx, ChangeRecorder? rec = null) : ILinkRepository
{
    const string TextSource = "text", ExplicitSource = "explicit";

    public Task<IReadOnlyList<TodoLink>> ListForItemAsync(Guid itemId) => ListForItemsAsync([itemId]);

    public async Task<IReadOnlyList<TodoLink>> ListForItemsAsync(IReadOnlyCollection<Guid> itemIds)
    {
        var links = new List<TodoLink>();
        foreach (var chunk in itemIds.Chunk(500))
        {
            var names = string.Join(",", chunk.Select((_, i) => $"$p{i}"));
            using var cmd = Sql.Cmd(conn, tx,
                $"SELECT * FROM todo_link WHERE deleted_at IS NULL AND item_id IN ({names}) ORDER BY item_id, position",
                chunk.Select((g, i) => ($"$p{i}", (object)g.ToString())).ToArray());
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                links.Add(new TodoLink(r.Guid("id")!.Value, r.Guid("item_id")!.Value, r.Str("url")!, r.Int("position")!.Value,
                    r.Instant("created_at")!.Value, r.Str("source")!));
        }
        return links;
    }

    public async Task<IReadOnlyList<Guid>> ListItemIdsForUrlAsync(string url)
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT DISTINCT item_id FROM todo_link WHERE url = $url AND deleted_at IS NULL", ("$url", url));
        using var r = await cmd.ExecuteReaderAsync();
        var ids = new List<Guid>();
        while (await r.ReadAsync()) ids.Add(r.Guid("item_id")!.Value);
        return ids;
    }

    public async Task ReplaceTextLinksAsync(Guid itemId, IReadOnlyList<string> urls, DateTimeOffset now)
    {
        var existing = (await ListForItemAsync(itemId)).Where(l => l.Source == TextSource).ToList();
        foreach (var gone in existing.Where(l => !urls.Contains(l.Url)))
            await RemoveAsync(gone.Id, now);
        for (var i = 0; i < urls.Count; i++)
            await UpsertAsync(itemId, urls[i], i, TextSource, now);
    }

    public Task AddExplicitAsync(Guid itemId, string url, DateTimeOffset now) =>
        UpsertAsync(itemId, url, position: 1000, ExplicitSource, now);

    public async Task RemoveAsync(Guid linkId, DateTimeOffset now)
    {
        var before = rec?.Active == true ? await rec.LoadAsync(EntityTypes.TodoLink, linkId) : null;
        using var cmd = Sql.Cmd(conn, tx, "UPDATE todo_link SET deleted_at = $now WHERE id = $id",
            ("$id", linkId.ToString()), ("$now", Sql.Val(now)));
        await cmd.ExecuteNonQueryAsync();
        if (rec?.Active == true) await rec.RecordRowStoreAsync(EntityTypes.TodoLink, linkId, before);
    }

    // Re-adding a tombstoned link revives it; an existing explicit link keeps its source.
    async Task UpsertAsync(Guid itemId, string url, int position, string source, DateTimeOffset now)
    {
        var linkId = Uuid5.Create(itemId, url);
        var before = rec?.Active == true ? await rec.LoadAsync(EntityTypes.TodoLink, linkId) : null;
        using var cmd = Sql.Cmd(conn, tx, """
            INSERT INTO todo_link (id, item_id, url, position, source, created_at)
            VALUES ($id, $item, $url, $pos, $source, $now)
            ON CONFLICT(id) DO UPDATE SET position = $pos, deleted_at = NULL,
                source = CASE WHEN source = 'explicit' THEN source ELSE $source END
            """,
            ("$id", Uuid5.Create(itemId, url).ToString()), ("$item", itemId.ToString()), ("$url", url),
            ("$pos", position), ("$source", source), ("$now", Sql.Val(now)));
        await cmd.ExecuteNonQueryAsync();
        if (rec?.Active == true) await rec.RecordRowStoreAsync(EntityTypes.TodoLink, linkId, before);
    }
}
