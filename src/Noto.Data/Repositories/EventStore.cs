using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Models;

namespace Noto.Data.Repositories;

sealed class EventStore(SqliteConnection conn, SqliteTransaction? tx, ChangeRecorder? rec = null) : IEventStore
{
    public async Task AppendAsync(ItemEvent e)
    {
        using var cmd = Sql.Cmd(conn, tx,
            "INSERT INTO item_event (id, item_id, workspace_id, type, data, occurred_at, tz, device_id, hlc) " +
            "VALUES ($id, $item, $ws, $type, $data, $at, $tz, $device, $hlc)",
            ("$id", e.Id.ToString()), ("$item", e.ItemId.ToString()), ("$ws", e.WorkspaceId.ToString()),
            ("$type", e.Type.ToString()), ("$data", Sql.Val(e.Data?.ToJsonString())), ("$at", Sql.Val(e.OccurredAt)),
            ("$tz", e.Tz), ("$device", e.DeviceId.ToString()), ("$hlc", e.Hlc));
        await cmd.ExecuteNonQueryAsync();
        if (rec?.Active == true) await rec.RecordEventAsync(e);
    }

    public Task<IReadOnlyList<ItemEvent>> ListForItemAsync(Guid itemId) =>
        QueryAsync("WHERE item_id = $p0", itemId.ToString());

    public Task<IReadOnlyList<ItemEvent>> ListForWorkspaceAsync(Guid workspaceId) =>
        QueryAsync("WHERE workspace_id = $p0", workspaceId.ToString());

    // Chunked to stay under SQLite's bound-parameter limit.
    public async Task<IReadOnlyList<ItemEvent>> ListForItemsAsync(IReadOnlyCollection<Guid> itemIds)
    {
        var all = new List<ItemEvent>();
        foreach (var chunk in itemIds.Chunk(500))
        {
            var names = string.Join(",", chunk.Select((_, i) => $"$p{i}"));
            all.AddRange(await QueryAsync($"WHERE item_id IN ({names})", chunk.Select(g => g.ToString()).ToArray()));
        }
        return all;
    }

    async Task<IReadOnlyList<ItemEvent>> QueryAsync(string where, params string[] args)
    {
        using var cmd = Sql.Cmd(conn, tx, $"SELECT * FROM item_event {where} ORDER BY occurred_at, rowid",
            args.Select((a, i) => ($"$p{i}", (object)a)).ToArray());
        using var r = await cmd.ExecuteReaderAsync();
        var events = new List<ItemEvent>();
        while (await r.ReadAsync())
            events.Add(new ItemEvent
            {
                Id = r.Guid("id")!.Value,
                ItemId = r.Guid("item_id")!.Value,
                WorkspaceId = r.Guid("workspace_id")!.Value,
                Type = r.Enum<ItemEventType>("type")!.Value,
                Data = r.Str("data") is { } json ? JsonNode.Parse(json)!.AsObject() : null,
                OccurredAt = r.Instant("occurred_at")!.Value,
                Tz = r.Str("tz")!,
                DeviceId = r.Guid("device_id")!.Value,
                Hlc = r.Str("hlc")!,
            });
        return events;
    }
}
