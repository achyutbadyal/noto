using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Sync;

namespace Noto.Data.Repositories;

// Records ops for synced writes made through the repositories, so services and commands can't bypass the
// op log. Suppressed while remote changes are being merged (those must not echo back as local edits).
sealed class ChangeRecorder(OpRecorder recorder, SqliteConnection conn, SqliteTransaction? tx, ISyncStore sync)
{
    int _suppressed;

    public SyncRowStore Rows { get; } = new(conn, tx);
    public bool Active => _suppressed == 0;

    public IDisposable Suppress()
    {
        _suppressed++;
        return new Release(this);
    }

    sealed class Release(ChangeRecorder owner) : IDisposable
    {
        bool _done;
        public void Dispose() { if (!_done) { _done = true; owner._suppressed--; } }
    }

    public async Task RecordAsync(string type, Guid id, Guid workspaceId, JsonObject? before, JsonObject after, bool? synced = null)
    {
        await recorder.RecordRowAsync(sync, type, id, workspaceId, before, after, synced ?? await WorkspaceSyncedAsync(workspaceId));
        await sync.SetStateAsync("hlc", recorder.Last);
    }

    public async Task RecordEventAsync(ItemEvent e)
    {
        await recorder.RecordEventAsync(sync, EntityTypes.ItemEvent, e.Id, e.WorkspaceId, SyncRows.ToRow(e), e.Hlc, await WorkspaceSyncedAsync(e.WorkspaceId));
        await sync.SetStateAsync("hlc", recorder.Last);
    }

    // Rule/tag/day-note/link writes: load the row before and after the SQL write and record the difference.
    public async Task<JsonObject?> LoadAsync(string type, Guid id) => await Rows.LoadAsync(type, id);

    public async Task RecordRowStoreAsync(string type, Guid id, JsonObject? before)
    {
        var after = await Rows.LoadAsync(type, id);
        if (after is null) return;
        var workspaceId = after["workspace_id"] is { } ws
            ? Guid.Parse(ws.GetValue<string>())
            : await WorkspaceOfItemAsync(Guid.Parse(after["item_id"]!.GetValue<string>()));
        if (workspaceId is { } w) await RecordAsync(type, id, w, before, after);
    }

    // Tag assignments are rows keyed by (item, tag); removal is a tombstone so it can sync.
    public async Task RecordItemTagsAsync(Guid itemId, IReadOnlyCollection<Guid> before, IReadOnlyCollection<Guid> after)
    {
        if (await WorkspaceOfItemAsync(itemId) is not { } workspaceId) return;
        foreach (var tag in after.Except(before)) await RecordTagAsync(itemId, tag, workspaceId, removed: false);
        foreach (var tag in before.Except(after)) await RecordTagAsync(itemId, tag, workspaceId, removed: true);
    }

    async Task RecordTagAsync(Guid itemId, Guid tagId, Guid workspaceId, bool removed)
    {
        var id = SyncRowStore.TodoTagId(itemId, tagId);
        var previous = await sync.GetRawAsync(EntityTypes.TodoTag, id);
        var row = new JsonObject
        {
            ["id"] = id.ToString(), ["item_id"] = itemId.ToString(), ["tag_id"] = tagId.ToString(),
            ["removed_at"] = removed ? DateTimeOffset.UtcNow.ToString("O") : null,
        };
        // A tag that was never recorded is new; otherwise this is a state flip of the existing row.
        var before = previous ?? (removed ? new JsonObject { ["id"] = id.ToString(), ["item_id"] = itemId.ToString(), ["tag_id"] = tagId.ToString(), ["removed_at"] = null } : null);
        await RecordAsync(EntityTypes.TodoTag, id, workspaceId, before, row);
    }

    async Task<bool> WorkspaceSyncedAsync(Guid workspaceId)
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT sync_enabled FROM workspace WHERE id = $id", ("$id", workspaceId.ToString()));
        return await cmd.ExecuteScalarAsync() is long enabled && enabled != 0;
    }

    async Task<Guid?> WorkspaceOfItemAsync(Guid itemId)
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT workspace_id FROM todo_item WHERE id = $id", ("$id", itemId.ToString()));
        return await cmd.ExecuteScalarAsync() is string ws ? Guid.Parse(ws) : null;
    }
}
