using System.Text.Json.Nodes;
using Noto.Core.Interfaces;

namespace Noto.Core.Sync;

// Bridges a synced entity type to local storage as JSON rows.
public interface IEntityCodec
{
    string EntityType { get; }
    Task<JsonObject?> LoadAsync(IStore store, Guid id);
    Task SaveAsync(IStore store, JsonObject row);
    Task<IReadOnlyList<JsonObject>> ListAsync(IStore store, Guid workspaceId);
}

// One entry per synced entity type: its field list and its storage codec. EntityTypes.All, SyncRows.FieldsOf
// and the merge/snapshot codecs are all derived from this list, so a type can't be half-registered.
// (Recording local writes still happens in the repository; SyncEntityTests checks every type has an entry.)
public sealed record SyncEntity(string Type, IReadOnlyList<string> Fields, IEntityCodec Codec);

public static class SyncEntities
{
    // Parents before children, so snapshots and first-time pushes read naturally (FKs are deferred anyway).
    public static IReadOnlyList<SyncEntity> All { get; } =
    [
        new(EntityTypes.Workspace, SyncRows.WorkspaceFields, new WorkspaceCodec()),
        RowStore(EntityTypes.Tag, SyncRows.TagFields),
        RowStore(EntityTypes.RecurrenceRule, SyncRows.RuleFields),
        new(EntityTypes.TodoItem, SyncRows.ItemFields, new ItemCodec()),
        RowStore(EntityTypes.TodoTag, SyncRows.TodoTagFields),
        RowStore(EntityTypes.TodoLink, SyncRows.LinkFields),
        RowStore(EntityTypes.DayNote, SyncRows.DayNoteFields),
        new(EntityTypes.ItemEvent, SyncRows.EventFields, new EventCodec()),
    ];

    public static IReadOnlyDictionary<string, SyncEntity> ByType { get; } =
        All.ToDictionary(e => e.Type);

    public static IReadOnlyDictionary<string, IEntityCodec> Codecs { get; } =
        All.ToDictionary(e => e.Type, e => e.Codec);

    static SyncEntity RowStore(string type, IReadOnlyList<string> fields) =>
        new(type, fields, new RowStoreCodec(type));

    // Rules, tags, tag assignments, day notes and links are plain SQL rows behind ISyncRowStore.
    sealed class RowStoreCodec(string entityType) : IEntityCodec
    {
        public string EntityType => entityType;

        public Task<JsonObject?> LoadAsync(IStore store, Guid id) =>
            store.SyncRows.LoadAsync(entityType, id);

        public Task SaveAsync(IStore store, JsonObject row) =>
            store.SyncRows.SaveAsync(entityType, row);

        public Task<IReadOnlyList<JsonObject>> ListAsync(IStore store, Guid workspaceId) =>
            store.SyncRows.ListAsync(entityType, workspaceId);
    }

    sealed class WorkspaceCodec : IEntityCodec
    {
        public string EntityType => EntityTypes.Workspace;

        public async Task<JsonObject?> LoadAsync(IStore store, Guid id) =>
            await store.Workspaces.GetAsync(id) is { } w ? SyncRows.ToRow(w) : null;

        // sync_enabled is device-local: keep it for known workspaces, enable it for ones first seen via sync.
        public async Task SaveAsync(IStore store, JsonObject row)
        {
            var existing = await store.Workspaces.GetAsync(
                Guid.Parse(row["id"]!.GetValue<string>())
            );
            await store.Workspaces.UpsertAsync(
                SyncRows.ToWorkspace(row, existing?.SyncEnabled ?? true)
            );
        }

        public async Task<IReadOnlyList<JsonObject>> ListAsync(IStore store, Guid workspaceId) =>
            await store.Workspaces.GetAsync(workspaceId) is { } w ? [SyncRows.ToRow(w)] : [];
    }

    sealed class ItemCodec : IEntityCodec
    {
        public string EntityType => EntityTypes.TodoItem;

        public async Task<JsonObject?> LoadAsync(IStore store, Guid id) =>
            await store.Items.GetAsync(id) is { } i ? SyncRows.ToRow(i) : null;

        public Task SaveAsync(IStore store, JsonObject row) =>
            store.Items.UpsertAsync(SyncRows.ToItem(row));

        public async Task<IReadOnlyList<JsonObject>> ListAsync(IStore store, Guid workspaceId) =>
            (await store.Items.ListAllAsync(workspaceId)).Select(SyncRows.ToRow).ToList();
    }

    sealed class EventCodec : IEntityCodec
    {
        public string EntityType => EntityTypes.ItemEvent;

        public Task<JsonObject?> LoadAsync(IStore store, Guid id) =>
            Task.FromResult<JsonObject?>(null);

        public Task SaveAsync(IStore store, JsonObject row) =>
            store.Events.AppendAsync(SyncRows.ToEvent(row));

        public async Task<IReadOnlyList<JsonObject>> ListAsync(IStore store, Guid workspaceId) =>
            (await store.Events.ListForWorkspaceAsync(workspaceId)).Select(SyncRows.ToRow).ToList();
    }
}
