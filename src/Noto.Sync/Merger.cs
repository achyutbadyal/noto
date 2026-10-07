using System.Text.Json.Nodes;
using Noto.Core.Interfaces;
using Noto.Core.Sync;
using Noto.Core.Time;

namespace Noto.Sync;

// Applies remote ops to local rows with per-field LWW, then repairs cross-field invariants deterministically.
// Ops are folded into per-entity state first and written once per entity, so a row built from several
// per-field ops (a snapshot) is never saved half-populated.
public sealed class Merger(IReadOnlyDictionary<string, IEntityCodec> codecs, HybridClock clock, TimeProvider time)
{
    sealed class Entry(string type, Guid id, Guid workspaceId, JsonObject raw, Dictionary<string, string> clocks)
    {
        public string Type = type;
        public Guid Id = id;
        public Guid WorkspaceId = workspaceId;
        public JsonObject Raw = raw;
        public Dictionary<string, string> Clocks = clocks;
        public HashSet<string> Won = [];
    }

    public async Task ApplyAsync(IStore store, IEnumerable<Op> ops)
    {
        // Remote changes must not echo back as local edits.
        using var _ = store.Sync.SuppressRecording();
        await store.Sync.DeferForeignKeysAsync();

        var batch = new Dictionary<(string, Guid), Entry>();
        var earliest = new Dictionary<Guid, DateOnly>();       // workspace → first logical day whose stats changed
        var touchedItems = new HashSet<Guid>();

        foreach (var op in ops)
        {
            if (Hlc.TryParse(op.Hlc, out var hlc)) clock.Receive(hlc);
            if (!codecs.TryGetValue(op.EntityType, out var codec)) continue;

            if (EntityTypes.IsImmutable(op.EntityType))
            {
                await FlushAsync(store, batch, earliest, touchedItems); // events need their item row to exist
                await ApplyInsertAsync(store, codec, op, earliest);
            }
            else await FoldAsync(store, codec, op, batch);
        }
        await FlushAsync(store, batch, earliest, touchedItems);

        foreach (var (workspaceId, from) in earliest) await store.Caches.InvalidateDayStatsFromAsync(workspaceId, from);
        foreach (var id in touchedItems) await store.Caches.InvalidateMetricsAsync(id);
    }

    // Immutable rows (events) are idempotent by id.
    async Task ApplyInsertAsync(IStore store, IEntityCodec codec, Op op, Dictionary<Guid, DateOnly> earliest)
    {
        if (op.Value is not JsonObject row) return;
        if (await store.Workspaces.GetAsync(op.WorkspaceId) is not { } ws) return;
        if (await store.Items.GetAsync(Guid.Parse(row["item_id"]!.GetValue<string>())) is null) return;
        if (await EventExistsAsync(store, row)) return;
        await codec.SaveAsync(store, row);

        var day = LogicalDate.Of(DateTimeOffset.Parse(row["occurred_at"]!.GetValue<string>()), row["tz"]!.GetValue<string>(), ws.DayBoundary);
        Earliest(earliest, op.WorkspaceId, day);
    }

    static async Task<bool> EventExistsAsync(IStore store, JsonObject row)
    {
        var id = Guid.Parse(row["id"]!.GetValue<string>());
        var events = await store.Events.ListForItemAsync(Guid.Parse(row["item_id"]!.GetValue<string>()));
        return events.Any(e => e.Id == id);
    }

    async Task FoldAsync(IStore store, IEntityCodec codec, Op op, Dictionary<(string, Guid), Entry> batch)
    {
        // A row can't exist without its workspace; those ops arrive again with the snapshot.
        if (op.EntityType != EntityTypes.Workspace
            && !batch.ContainsKey((EntityTypes.Workspace, op.WorkspaceId))
            && await store.Workspaces.GetAsync(op.WorkspaceId) is null) return;

        if (!batch.TryGetValue((op.EntityType, op.EntityId), out var entry))
        {
            var raw = await store.Sync.GetRawAsync(op.EntityType, op.EntityId)
                      ?? await codec.LoadAsync(store, op.EntityId)
                      ?? new JsonObject { ["id"] = op.EntityId.ToString() };
            entry = new Entry(op.EntityType, op.EntityId, op.WorkspaceId, raw, await store.Sync.GetClocksAsync(op.EntityType, op.EntityId));
            batch[(op.EntityType, op.EntityId)] = entry;
        }

        var before = entry.Clocks.ToDictionary();
        var outcomes = LwwRow.Apply(op, entry.Raw, entry.Clocks);
        foreach (var o in outcomes)
        {
            await LogConflictAsync(store, op, o, before);
            if (o.Applied) entry.Won.Add(o.Field);
        }
    }

    async Task FlushAsync(IStore store, Dictionary<(string, Guid), Entry> batch, Dictionary<Guid, DateOnly> earliest, HashSet<Guid> touchedItems)
    {
        foreach (var entry in batch.Values.Where(e => e.Won.Count > 0).OrderBy(e => Array.IndexOf(EntityTypes.All.ToArray(), e.Type)))
        {
            // The typed row is a normalized view of the raw LWW state; raw is what converges.
            await store.Sync.SetRawAsync(entry.Type, entry.Id, entry.Raw);
            var row = (JsonObject)entry.Raw.DeepClone();
            if (entry.Type == EntityTypes.TodoItem) RowNormalizer.Item(row);
            await codecs[entry.Type].SaveAsync(store, row);
            foreach (var field in entry.Won) await store.Sync.SetClockAsync(entry.Type, entry.Id, field, entry.Clocks[field]);

            if (entry.Type == EntityTypes.TodoItem)
            {
                touchedItems.Add(entry.Id);
                if (entry.Won.Contains("completed_on") && row["completed_on"]?.GetValue<string>() is { } credited)
                    Earliest(earliest, entry.WorkspaceId, DateOnly.Parse(credited));
            }
        }
        batch.Clear();
    }

    // Keeps the losing side of a concurrent text edit so nothing is silently lost (UC-05).
    async Task LogConflictAsync(IStore store, Op op, FieldOutcome o, Dictionary<string, string> clocksBefore)
    {
        if (!EntityTypes.IsTextField(op.EntityType, o.Field)) return;
        var remote = op.Kind == OpKinds.Set ? op.Value : (op.Value as JsonObject)?[o.Field];

        if (!o.Applied)
        {
            // Remote lost: it was written without seeing our newer value.
            if (!JsonNode.DeepEquals(o.Previous, remote) && Hlc.Compare(op.Hlc, clocksBefore.GetValueOrDefault(o.Field)) < 0)
                await Record(remote, op.Hlc);
        }
        else if (!JsonNode.DeepEquals(o.Previous, remote)
                 && clocksBefore.TryGetValue(o.Field, out var local)
                 && Hlc.Parse(local).Device == clock.Device
                 && await store.Sync.HasPendingForFieldAsync(op.EntityType, op.EntityId, o.Field))
        {
            // Remote won over a local edit that hadn't been pushed yet.
            await Record(o.Previous, local);
        }

        Task Record(JsonNode? value, string hlc) => store.Sync.AddConflictAsync(new SyncConflict(
            Guid.CreateVersion7(), op.EntityType, op.EntityId, o.Field, value?.GetValue<string>(), hlc, time.GetUtcNow()));
    }

    static void Earliest(Dictionary<Guid, DateOnly> map, Guid workspaceId, DateOnly day)
    {
        if (!map.TryGetValue(workspaceId, out var current) || day < current) map[workspaceId] = day;
    }
}
