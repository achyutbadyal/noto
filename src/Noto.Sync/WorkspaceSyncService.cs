using System.Text.Json.Nodes;
using Noto.Core.Interfaces;
using Noto.Core.Sync;

namespace Noto.Sync;

// Per-workspace sync toggle and snapshot bootstrap (docs/05 › Sync).
public sealed class WorkspaceSyncService(
    IUnitOfWork uow,
    ISyncTransport transport,
    Merger merger,
    Guid deviceId,
    SyncOptions? options = null
)
{
    readonly SyncOptions _options = options ?? new SyncOptions();

    // Loads the server copy (if any), turns sync on, then queues every local row with its original field
    // clocks. Per-field LWW merges both sides, so nothing is overwritten wholesale.
    public async Task EnableAsync(Guid workspaceId, CancellationToken ct = default)
    {
        await LoadSnapshotAsync(workspaceId, ct);

        await uow.RunAsync(async store =>
        {
            var ws =
                await store.Workspaces.GetAsync(workspaceId)
                ?? throw new InvalidOperationException("Workspace not found");
            ws.SyncEnabled = true;
            await store.Workspaces.UpsertAsync(ws);
            await store.Sync.ClearPendingAsync(workspaceId);

            foreach (var type in EntityTypes.All)
            {
                var codec = SyncEntities.Codecs[type];
                foreach (var row in await codec.ListAsync(store, workspaceId))
                foreach (var op in await OpsForRowAsync(store, type, workspaceId, row))
                    await store.Sync.AddPendingAsync(op);
            }
            return 0;
        });
    }

    // Stops pushing and pulling. The server copy stays unless the user asks to remove it; local data is untouched.
    public async Task DisableAsync(
        Guid workspaceId,
        bool removeFromServer,
        CancellationToken ct = default
    )
    {
        await uow.RunAsync(async store =>
        {
            var ws =
                await store.Workspaces.GetAsync(workspaceId)
                ?? throw new InvalidOperationException("Workspace not found");
            ws.SyncEnabled = false;
            await store.Workspaces.UpsertAsync(ws);
            await store.Sync.ClearPendingAsync(workspaceId);
            return 0;
        });
        if (removeFromServer)
            await transport.DeleteWorkspaceAsync(workspaceId, ct);
    }

    // New device: pull every workspace the account has, then start the cursor at the oldest snapshot.
    public async Task BootstrapNewDeviceAsync(CancellationToken ct = default)
    {
        var ids = await transport.ListWorkspacesAsync(ct);
        long? floor = null;
        foreach (var id in ids)
        {
            var seq = await LoadSnapshotAsync(id, ct);
            floor = floor is null ? seq : Math.Min(floor.Value, seq);
        }
        if (floor is { } cursor)
            await uow.RunAsync(async s =>
            {
                await s.Sync.SetStateAsync(SyncClient.CursorKey, cursor.ToString());
                return 0;
            });
    }

    // The server lost history (409): restart from zero and re-snapshot everything we sync.
    public async Task RebootstrapAsync(CancellationToken ct = default)
    {
        await uow.RunAsync(async s =>
        {
            await s.Sync.SetStateAsync(SyncClient.CursorKey, "0");
            return 0;
        });
        var enabled = await uow.RunAsync(async s =>
            (await s.Workspaces.ListAsync()).Where(w => w.SyncEnabled).Select(w => w.Id).ToList()
        );
        long? floor = null;
        foreach (var id in enabled)
        {
            var seq = await LoadSnapshotAsync(id, ct);
            floor = floor is null ? seq : Math.Min(floor.Value, seq);
        }
        if (floor is { } cursor)
            await uow.RunAsync(async s =>
            {
                await s.Sync.SetStateAsync(SyncClient.CursorKey, cursor.ToString());
                return 0;
            });
    }

    // Returns the seq of the first page, a safe lower bound for what the snapshot contains.
    async Task<long> LoadSnapshotAsync(Guid workspaceId, CancellationToken ct)
    {
        long? first = null;
        foreach (var type in EntityTypes.All)
        {
            string? after = null;
            do
            {
                var page = await transport.SnapshotAsync(
                    workspaceId,
                    type,
                    after,
                    _options.SnapshotPage,
                    ct
                );
                first ??= page.Seq;
                var ops = page.Rows.SelectMany(r => SnapshotOps(type, workspaceId, r)).ToList();
                await uow.RunAsync(async store =>
                {
                    await merger.ApplyAsync(store, ops);
                    return 0;
                });
                after = page.NextAfter;
            } while (after is not null);
        }
        return first ?? 0;
    }

    // A snapshot row replays as per-field sets at their recorded clocks, so the merge rule is the same as for live ops.
    IEnumerable<Op> SnapshotOps(string type, Guid workspaceId, SnapshotRow r)
    {
        if (EntityTypes.IsImmutable(type))
        {
            var hlc = r.Row["hlc"]?.GetValue<string>() ?? Hlc.Zero(Guid.Empty).ToString();
            yield return new Op(
                Guid.CreateVersion7(),
                type,
                r.Id,
                workspaceId,
                OpKinds.Insert,
                null,
                r.Row,
                hlc,
                Guid.Empty
            );
            yield break;
        }
        foreach (var (field, hlc) in r.FieldClocks)
            yield return new Op(
                Guid.CreateVersion7(),
                type,
                r.Id,
                workspaceId,
                OpKinds.Set,
                field,
                r.Row[field]?.DeepClone(),
                hlc,
                Guid.Empty
            );
    }

    async Task<IEnumerable<Op>> OpsForRowAsync(
        IStore store,
        string type,
        Guid workspaceId,
        JsonObject row
    )
    {
        var id = Guid.Parse(row["id"]!.GetValue<string>());
        if (EntityTypes.IsImmutable(type))
        {
            var hlc = row["hlc"]?.GetValue<string>() is { Length: > 0 } h
                ? h
                : Hlc.Zero(deviceId).ToString();
            return
            [
                new Op(
                    Guid.CreateVersion7(),
                    type,
                    id,
                    workspaceId,
                    OpKinds.Insert,
                    null,
                    row,
                    hlc,
                    deviceId
                ),
            ];
        }

        var clocks = await store.Sync.GetClocksAsync(type, id);
        var ops = new List<Op>();
        foreach (var field in SyncRows.FieldsOf(type)!)
        {
            // Null fields with no clock are the default; nothing to say about them.
            if (row[field] is null && !clocks.ContainsKey(field))
                continue;
            var hlc = clocks.GetValueOrDefault(field) ?? Hlc.Zero(deviceId).ToString();
            ops.Add(
                new Op(
                    Guid.CreateVersion7(),
                    type,
                    id,
                    workspaceId,
                    OpKinds.Set,
                    field,
                    row[field]?.DeepClone(),
                    hlc,
                    deviceId
                )
            );
        }
        return ops;
    }
}
