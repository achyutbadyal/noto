using System.Text.Json;
using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Links;

namespace Noto.Data.Repositories;

sealed class PreviewCacheRepository(SqliteConnection conn, SqliteTransaction? tx) : IPreviewCache
{
    public async Task<LinkPreview?> GetAsync(string url) =>
        (await GetManyAsync([url])).GetValueOrDefault(url);

    public async Task<IReadOnlyDictionary<string, LinkPreview>> GetManyAsync(
        IReadOnlyCollection<string> urls
    )
    {
        var map = new Dictionary<string, LinkPreview>();
        foreach (var chunk in urls.Chunk(500))
        {
            var names = string.Join(",", chunk.Select((_, i) => $"$p{i}"));
            using var cmd = Sql.Cmd(
                conn,
                tx,
                $"SELECT * FROM link_preview_cache WHERE url IN ({names})",
                chunk.Select((u, i) => ($"$p{i}", (object)u)).ToArray()
            );
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                map[r.Str("url")!] = Map(r);
        }
        return map;
    }

    public async Task PutAsync(LinkPreview p)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            """
            INSERT INTO link_preview_cache (url, provider_id, connection_id, fields, state_hash, preview_status, fetched_at,
                expires_at, user_last_viewed_at, viewed_state_hash)
            VALUES ($url, $provider, $conn, $fields, $hash, $status, $fetched, $expires, $viewedAt, $viewedHash)
            ON CONFLICT(url) DO UPDATE SET provider_id=$provider, connection_id=$conn, fields=$fields, state_hash=$hash,
                preview_status=$status, fetched_at=$fetched, expires_at=$expires
            """,
            ("$url", p.Url),
            ("$provider", p.ProviderId),
            ("$conn", Sql.Val(p.ConnectionId)),
            ("$fields", JsonSerializer.Serialize(p, LinkJson.Default.LinkPreview)),
            ("$hash", Sql.Val(p.StateHash)),
            ("$status", p.Status.ToString()),
            ("$fetched", Sql.Val(p.FetchedAt)),
            ("$expires", Sql.Val(p.ExpiresAt)),
            ("$viewedAt", Sql.Val(p.UserLastViewedAt)),
            ("$viewedHash", Sql.Val(p.ViewedStateHash))
        );
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkViewedAsync(string url, DateTimeOffset now)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "UPDATE link_preview_cache SET viewed_state_hash = state_hash, user_last_viewed_at = $now WHERE url = $url",
            ("$url", url),
            ("$now", Sql.Val(now))
        );
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteForConnectionAsync(Guid connectionId)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "DELETE FROM link_preview_cache WHERE connection_id = $id",
            ("$id", connectionId.ToString())
        );
        await cmd.ExecuteNonQueryAsync();
    }

    // Viewed columns are authoritative in their own columns, not in the serialized fields.
    static LinkPreview Map(SqliteDataReader r)
    {
        var p = JsonSerializer.Deserialize(r.Str("fields")!, LinkJson.Default.LinkPreview)!;
        p.ViewedStateHash = r.Str("viewed_state_hash");
        p.UserLastViewedAt = r.Instant("user_last_viewed_at");
        return p;
    }
}
