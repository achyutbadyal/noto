using Noto.Core.Sync;

namespace Noto.Core.Interfaces;

// Local sync bookkeeping (docs/05): pending ops, per-field clocks, cursor/state, conflict log.
// Plain SQL rows for the synced entity types that have no typed mapper (rules, tags, todo_tag, day notes, links),
// as JSON rows keyed by the sync field names.
public interface ISyncRowStore
{
    Task<System.Text.Json.Nodes.JsonObject?> LoadAsync(string entityType, Guid id);
    Task SaveAsync(string entityType, System.Text.Json.Nodes.JsonObject row);
    Task<IReadOnlyList<System.Text.Json.Nodes.JsonObject>> ListAsync(
        string entityType,
        Guid workspaceId
    );
}

public interface ISyncStore
{
    Task AddPendingAsync(Op op);
    Task<IReadOnlyList<Op>> ListPendingAsync(int limit);
    Task RemovePendingAsync(IReadOnlyCollection<Guid> opIds);
    Task ClearPendingAsync(Guid workspaceId);
    Task<bool> HasPendingForFieldAsync(string entityType, Guid entityId, string field);

    Task<Dictionary<string, string>> GetClocksAsync(string entityType, Guid entityId);
    Task SetClockAsync(string entityType, Guid entityId, string field, string hlc);

    // The un-normalized LWW row; null until the first synced change to the entity.
    Task<System.Text.Json.Nodes.JsonObject?> GetRawAsync(string entityType, Guid entityId);
    Task SetRawAsync(string entityType, Guid entityId, System.Text.Json.Nodes.JsonObject row);

    Task<string?> GetStateAsync(string key);
    Task SetStateAsync(string key, string value);

    Task AddConflictAsync(SyncConflict conflict);
    Task<IReadOnlyList<SyncConflict>> ListConflictsAsync(Guid entityId);
    Task DismissConflictsAsync(Guid entityId);

    // While suppressed, repository writes are not recorded as ops (used when applying remote changes).
    IDisposable SuppressRecording();

    // Rows can reference each other in any order during a snapshot load.
    Task DeferForeignKeysAsync();

    // Hard-deletes items tombstoned before `before`, with their events (only safe once peers have pulled past them).
    Task<int> PurgeTombstonedItemsAsync(Guid workspaceId, DateTimeOffset before);
}
