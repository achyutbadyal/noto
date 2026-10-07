using Noto.Core.Interfaces;
using Noto.Data.Repositories;

namespace Noto.Data;

public sealed partial class SqliteUnitOfWork : ISearchIndex
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        Guid? workspaceId = null,
        int limit = 20
    )
    {
        var match = ToMatch(query);
        if (match is null)
            return [];

        await _gate.WaitAsync();
        try
        {
            using var cmd = Sql.Cmd(
                _conn,
                null,
                """
                SELECT i.id, i.workspace_id, i.title, snippet(item_fts, -1, '[', ']', '…', 8) AS snip
                FROM item_fts JOIN todo_item i ON i.rowid = item_fts.rowid
                WHERE item_fts MATCH $q AND i.deleted_at IS NULL AND ($ws IS NULL OR i.workspace_id = $ws)
                ORDER BY rank LIMIT $limit
                """,
                ("$q", match),
                ("$ws", Sql.Val(workspaceId)),
                ("$limit", limit)
            );
            using var r = await cmd.ExecuteReaderAsync();
            var hits = new List<SearchHit>();
            while (await r.ReadAsync())
                hits.Add(
                    new SearchHit(
                        r.Guid("id")!.Value,
                        r.Guid("workspace_id")!.Value,
                        r.Str("title")!,
                        r.Str("snip")
                    )
                );
            return hits;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Each word becomes a quoted prefix term, so user input can never be parsed as FTS syntax.
    static string? ToMatch(string query)
    {
        var words = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0)
            .Select(w => $"\"{w}\"*");
        var joined = string.Join(' ', words);
        return joined.Length == 0 ? null : joined;
    }
}
