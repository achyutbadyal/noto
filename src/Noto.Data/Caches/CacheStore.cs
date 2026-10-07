using System.Text.Json;
using Microsoft.Data.Sqlite;
using Noto.Core.Derivations;
using Noto.Core.Interfaces;
using Noto.Data.Repositories;

namespace Noto.Data.Repositories;

sealed class CacheStore(SqliteConnection conn, SqliteTransaction? tx) : ICacheStore
{
    public async Task<IReadOnlyDictionary<DateOnly, DayStats>> GetDayStatsAsync(
        Guid workspaceId,
        DateOnly from,
        DateOnly to
    )
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "SELECT stats FROM day_stats_cache WHERE workspace_id = $ws AND day BETWEEN $from AND $to",
            ("$ws", workspaceId.ToString()),
            ("$from", Sql.Val(from)),
            ("$to", Sql.Val(to))
        );
        using var r = await cmd.ExecuteReaderAsync();
        var map = new Dictionary<DateOnly, DayStats>();
        while (await r.ReadAsync())
        {
            var stats = JsonSerializer.Deserialize(
                r.GetString(0),
                DerivationJson.Default.DayStats
            )!;
            map[stats.Day] = stats;
        }
        return map;
    }

    public async Task PutDayStatsAsync(Guid workspaceId, DayStats stats)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "INSERT OR REPLACE INTO day_stats_cache (workspace_id, day, stats) VALUES ($ws, $day, $stats)",
            ("$ws", workspaceId.ToString()),
            ("$day", Sql.Val(stats.Day)),
            ("$stats", JsonSerializer.Serialize(stats, DerivationJson.Default.DayStats))
        );
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InvalidateDayStatsFromAsync(Guid workspaceId, DateOnly from)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "DELETE FROM day_stats_cache WHERE workspace_id = $ws AND day >= $from",
            ("$ws", workspaceId.ToString()),
            ("$from", Sql.Val(from))
        );
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyDictionary<Guid, ItemMetrics>> GetMetricsAsync(
        IReadOnlyCollection<Guid> itemIds,
        DateOnly asOf
    )
    {
        var map = new Dictionary<Guid, ItemMetrics>();
        foreach (var chunk in itemIds.Chunk(500))
        {
            var names = string.Join(",", chunk.Select((_, i) => $"$p{i}"));
            var args = chunk
                .Select((g, i) => ($"$p{i}", (object)g.ToString()))
                .Append(("$asOf", Sql.Val(asOf)))
                .ToArray();
            using var cmd = Sql.Cmd(
                conn,
                tx,
                $"SELECT item_id, age, carry, defers FROM item_metrics_cache WHERE as_of = $asOf AND item_id IN ({names})",
                args
            );
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                map[Guid.Parse(r.GetString(0))] = new ItemMetrics(
                    r.GetInt32(1),
                    r.GetInt32(2),
                    r.GetInt32(3)
                );
        }
        return map;
    }

    public async Task PutMetricsAsync(Guid itemId, DateOnly asOf, ItemMetrics m)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "INSERT OR REPLACE INTO item_metrics_cache (item_id, as_of, age, carry, defers) VALUES ($id, $asOf, $age, $carry, $defers)",
            ("$id", itemId.ToString()),
            ("$asOf", Sql.Val(asOf)),
            ("$age", m.Age),
            ("$carry", m.Carry),
            ("$defers", m.Defers)
        );
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InvalidateMetricsAsync(Guid itemId)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "DELETE FROM item_metrics_cache WHERE item_id = $id",
            ("$id", itemId.ToString())
        );
        await cmd.ExecuteNonQueryAsync();
    }
}
