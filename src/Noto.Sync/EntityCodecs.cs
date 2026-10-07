using System.Text.Json.Nodes;
using Noto.Core.Interfaces;
using Noto.Core.Sync;

namespace Noto.Sync;

// Bridges a synced entity type to local storage as JSON rows.
public interface IEntityCodec
{
    string EntityType { get; }
    Task<JsonObject?> LoadAsync(IStore store, Guid id);
    Task SaveAsync(IStore store, JsonObject row);
    Task<IReadOnlyList<JsonObject>> ListAsync(IStore store, Guid workspaceId);
}

public static class EntityCodecs
{
    public static IReadOnlyDictionary<string, IEntityCodec> Default { get; } = new Dictionary<string, IEntityCodec>
    {
        [EntityTypes.Workspace] = new WorkspaceCodec(),
        [EntityTypes.TodoItem] = new ItemCodec(),
        [EntityTypes.ItemEvent] = new EventCodec(),
        [EntityTypes.RecurrenceRule] = new RowStoreCodec(EntityTypes.RecurrenceRule),
        [EntityTypes.Tag] = new RowStoreCodec(EntityTypes.Tag),
        [EntityTypes.TodoTag] = new RowStoreCodec(EntityTypes.TodoTag),
        [EntityTypes.DayNote] = new RowStoreCodec(EntityTypes.DayNote),
        [EntityTypes.TodoLink] = new RowStoreCodec(EntityTypes.TodoLink),
    };

    // Rules, tags, tag assignments, day notes and links are plain SQL rows behind ISyncRowStore.
    sealed class RowStoreCodec(string entityType) : IEntityCodec
    {
        public string EntityType => entityType;
        public Task<JsonObject?> LoadAsync(IStore store, Guid id) => store.SyncRows.LoadAsync(entityType, id);
        public Task SaveAsync(IStore store, JsonObject row) => store.SyncRows.SaveAsync(entityType, row);
        public Task<IReadOnlyList<JsonObject>> ListAsync(IStore store, Guid workspaceId) => store.SyncRows.ListAsync(entityType, workspaceId);
    }

    sealed class WorkspaceCodec : IEntityCodec
    {
        public string EntityType => EntityTypes.Workspace;

        public async Task<JsonObject?> LoadAsync(IStore store, Guid id) =>
            await store.Workspaces.GetAsync(id) is { } w ? SyncRows.ToRow(w) : null;

        // sync_enabled is device-local: keep it for known workspaces, enable it for ones first seen via sync.
        public async Task SaveAsync(IStore store, JsonObject row)
        {
            var existing = await store.Workspaces.GetAsync(Guid.Parse(row["id"]!.GetValue<string>()));
            await store.Workspaces.UpsertAsync(SyncRows.ToWorkspace(row, existing?.SyncEnabled ?? true));
        }

        public async Task<IReadOnlyList<JsonObject>> ListAsync(IStore store, Guid workspaceId) =>
            await store.Workspaces.GetAsync(workspaceId) is { } w ? [SyncRows.ToRow(w)] : [];
    }

    sealed class ItemCodec : IEntityCodec
    {
        public string EntityType => EntityTypes.TodoItem;

        public async Task<JsonObject?> LoadAsync(IStore store, Guid id) =>
            await store.Items.GetAsync(id) is { } i ? SyncRows.ToRow(i) : null;

        public Task SaveAsync(IStore store, JsonObject row) => store.Items.UpsertAsync(SyncRows.ToItem(row));

        public async Task<IReadOnlyList<JsonObject>> ListAsync(IStore store, Guid workspaceId) =>
            (await store.Items.ListAllAsync(workspaceId)).Select(SyncRows.ToRow).ToList();
    }

    sealed class EventCodec : IEntityCodec
    {
        public string EntityType => EntityTypes.ItemEvent;

        public Task<JsonObject?> LoadAsync(IStore store, Guid id) => Task.FromResult<JsonObject?>(null);
        public Task SaveAsync(IStore store, JsonObject row) => store.Events.AppendAsync(SyncRows.ToEvent(row));

        public async Task<IReadOnlyList<JsonObject>> ListAsync(IStore store, Guid workspaceId) =>
            (await store.Events.ListForWorkspaceAsync(workspaceId)).Select(SyncRows.ToRow).ToList();
    }
}
