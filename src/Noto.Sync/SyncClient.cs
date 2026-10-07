using Noto.Core.Interfaces;
using Noto.Core.Sync;

namespace Noto.Sync;

public sealed record SyncResult(int Pushed, int Pulled, int Rejected, bool Rebootstrapped);

// Push local ops and pull remote ones until both sides are drained. Sync failures never touch local data.
public sealed class SyncClient(
    IUnitOfWork uow, ISyncTransport transport, Merger merger, WorkspaceSyncService workspaces,
    HybridClock clock, Guid deviceId, SyncOptions? options = null)
{
    public const string CursorKey = "cursor";
    readonly SyncOptions _options = options ?? new SyncOptions();

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        int pushed = 0, pulled = 0, rejected = 0;
        var rebootstrapped = false;

        // Bounded so a misbehaving server can't spin us forever.
        for (var round = 0; round < 1000; round++)
        {
            var (pending, enabled, cursor) = await uow.RunAsync(async store => (
                await store.Sync.ListPendingAsync(_options.PushBatch),
                (await store.Workspaces.ListAsync()).Where(w => w.SyncEnabled).Select(w => w.Id).ToList(),
                long.Parse(await store.Sync.GetStateAsync(CursorKey) ?? "0")));

            if (enabled.Count == 0 && pending.Count == 0) break;

            SyncResponse response;
            try
            {
                response = await transport.SyncAsync(new SyncRequest(deviceId, cursor, enabled, _options.PullLimit, pending), ct);
            }
            catch (CursorAheadException)
            {
                await workspaces.RebootstrapAsync(ct);
                rebootstrapped = true;
                continue;
            }

            var remote = response.Ops.Where(o => o.DeviceId != deviceId).ToList();
            await uow.RunAsync(async store =>
            {
                // Merge first: a remote edit that beats a still-queued local edit is a conflict worth logging.
                await merger.ApplyAsync(store, remote);
                await store.Sync.RemovePendingAsync(response.AcceptedOpIds.Concat(response.Rejected.Select(r => r.OpId)).ToList());
                await store.Sync.SetStateAsync(CursorKey, response.NextCursor.ToString());
                await store.Sync.SetStateAsync("hlc", clock.Last);
                return 0;
            });

            pushed += response.AcceptedOpIds.Count;
            rejected += response.Rejected.Count;
            pulled += remote.Count;

            var morePending = pending.Count >= _options.PushBatch;
            if (!response.HasMore && !morePending) break;
        }
        return new SyncResult(pushed, pulled, rejected, rebootstrapped);
    }
}
