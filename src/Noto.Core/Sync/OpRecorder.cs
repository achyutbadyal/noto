using System.Text.Json.Nodes;
using Noto.Core.Interfaces;

namespace Noto.Core.Sync;

// Turns state changes into ops. Field clocks are always kept (so a workspace can enable sync later with
// its original clocks); ops are queued for push only while the workspace has sync enabled.
public sealed class OpRecorder(HybridClock clock)
{
    public Guid Device => clock.Device;
    public string Last => clock.Last;

    public async Task RecordEventAsync(
        ISyncStore sync,
        string eventType,
        Guid eventId,
        Guid workspaceId,
        JsonObject row,
        string hlc,
        bool syncEnabled
    )
    {
        if (!syncEnabled)
            return;
        await sync.AddPendingAsync(
            new Op(
                Guid.CreateVersion7(),
                eventType,
                eventId,
                workspaceId,
                OpKinds.Insert,
                null,
                row,
                string.IsNullOrEmpty(hlc) ? clock.Next().ToString() : hlc,
                clock.Device
            )
        );
    }

    // `before` null means the row is new: one insert op. Otherwise one set op per changed field.
    public async Task RecordRowAsync(
        ISyncStore sync,
        string type,
        Guid id,
        Guid workspaceId,
        JsonObject? before,
        JsonObject after,
        bool syncEnabled
    )
    {
        var fields = SyncRows.FieldsOf(type)!;
        if (before is null)
        {
            var hlc = clock.Next().ToString();
            foreach (var f in fields)
                await sync.SetClockAsync(type, id, f, hlc);
            await sync.SetRawAsync(type, id, (JsonObject)after.DeepClone());
            if (syncEnabled)
                await sync.AddPendingAsync(
                    new Op(
                        Guid.CreateVersion7(),
                        type,
                        id,
                        workspaceId,
                        OpKinds.Insert,
                        null,
                        after,
                        hlc,
                        clock.Device
                    )
                );
            return;
        }

        var raw = await sync.GetRawAsync(type, id) ?? (JsonObject)before.DeepClone();
        var changed = false;
        foreach (var (field, value) in SyncRows.Diff(before, after, fields))
        {
            var hlc = clock.Next().ToString();
            await sync.SetClockAsync(type, id, field, hlc);
            raw[field] = value?.DeepClone();
            changed = true;
            if (syncEnabled)
                await sync.AddPendingAsync(
                    new Op(
                        Guid.CreateVersion7(),
                        type,
                        id,
                        workspaceId,
                        OpKinds.Set,
                        field,
                        value,
                        hlc,
                        clock.Device
                    )
                );
        }
        if (changed)
            await sync.SetRawAsync(type, id, raw);
    }
}
