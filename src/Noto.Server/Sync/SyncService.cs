using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noto.Core.Sync;
using Noto.Server.Data;
using Noto.Server.Middleware;
using Noto.Sync;

namespace Noto.Server.Sync;

// One write path: /sync. Ops are idempotent by op_id, appended to the log with a monotonically increasing
// seq, and folded into current_rows with the same per-field LWW rule clients use.
public sealed class SyncService(ServerDbContext db, TimeProvider time, ServerClock clock)
{
    public const int MaxOpsPerPush = 2000;
    const int MaxValueChars = 64 * 1024;
    static readonly TimeSpan MaxClockDrift = TimeSpan.FromHours(24);

    // Seq must be visible in commit order, so ingest is serialized per process (single-instance server).
    static readonly SemaphoreSlim IngestLock = new(1, 1);

    public async Task<SyncResponse> SyncAsync(
        Guid userId,
        Guid deviceId,
        SyncRequest req,
        CancellationToken ct
    )
    {
        if (req.Ops.Count > MaxOpsPerPush)
            throw new ApiException(
                413,
                "PAYLOAD_TOO_LARGE",
                "Too many ops",
                $"At most {MaxOpsPerPush} ops per push"
            );

        await IngestLock.WaitAsync(ct);
        try
        {
            var maxSeq = await db.Ops.MaxAsync(o => (long?)o.Seq, ct) ?? 0;
            if (req.Cursor > maxSeq)
                throw new ApiException(
                    409,
                    "CURSOR_AHEAD",
                    "Cursor ahead of server",
                    "Re-bootstrap via /sync/snapshot"
                );

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var (accepted, rejected) = await IngestAsync(userId, deviceId, req.Ops, ct);
            maxSeq = await db.Ops.MaxAsync(o => (long?)o.Seq, ct) ?? 0;

            var (ops, nextCursor, hasMore) = await PullAsync(userId, deviceId, req, maxSeq, ct);

            var device = await db.Devices.FirstAsync(d => d.Id == deviceId, ct);
            device.Cursor = nextCursor;
            device.LastSyncAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new SyncResponse(accepted, rejected, ops, nextCursor, hasMore, clock.Now());
        }
        finally
        {
            IngestLock.Release();
        }
    }

    async Task<(List<Guid> Accepted, List<RejectedOp> Rejected)> IngestAsync(
        Guid userId,
        Guid deviceId,
        IReadOnlyList<Op> ops,
        CancellationToken ct
    )
    {
        var accepted = new List<Guid>();
        var rejected = new List<RejectedOp>();
        if (ops.Count == 0)
            return (accepted, rejected);

        var opIds = ops.Select(o => o.OpId).ToList();
        var known = (
            await db.Ops.Where(o => opIds.Contains(o.OpId)).Select(o => o.OpId).ToListAsync(ct)
        ).ToHashSet();

        var workspaceIds = ops.Select(o => o.WorkspaceId).Distinct().ToList();
        var owners = await db
            .WorkspaceSyncs.Where(w => workspaceIds.Contains(w.WorkspaceId))
            .ToDictionaryAsync(w => w.WorkspaceId, ct);

        var rows = new Dictionary<(string, string), CurrentRow>();
        var maxHlcMs = time.GetUtcNow().Add(MaxClockDrift).ToUnixTimeMilliseconds();

        foreach (var op in ops)
        {
            if (known.Contains(op.OpId))
            {
                accepted.Add(op.OpId);
                continue;
            } // retry of an applied op

            var code = Validate(op, maxHlcMs);
            if (
                code is null
                && owners.TryGetValue(op.WorkspaceId, out var owner)
                && owner.UserId != userId
            )
                code = "WORKSPACE_NOT_OWNED";
            if (code is not null)
            {
                rejected.Add(new RejectedOp(op.OpId, code));
                continue;
            }

            var key = (op.EntityType, op.EntityId.ToString());
            if (!rows.TryGetValue(key, out var row))
            {
                row = await db.CurrentRows.FindAsync([key.Item1, key.Item2], ct);
                if (row is not null)
                    rows[key] = row;
            }
            if (row is not null && row.UserId != userId)
            {
                rejected.Add(new RejectedOp(op.OpId, "ENTITY_NOT_OWNED"));
                continue;
            }

            // The first valid op for an unknown workspace claims it for this user.
            if (!owners.ContainsKey(op.WorkspaceId))
            {
                var claim = new WorkspaceSync { WorkspaceId = op.WorkspaceId, UserId = userId };
                db.WorkspaceSyncs.Add(claim);
                owners[op.WorkspaceId] = claim;
            }

            var log = new OpRow
            {
                OpId = op.OpId,
                UserId = userId,
                WorkspaceId = op.WorkspaceId,
                EntityType = op.EntityType,
                EntityId = op.EntityId.ToString(),
                Kind = op.Kind,
                Field = op.Field,
                Value = op.Value?.ToJsonString() ?? "null",
                Hlc = op.Hlc,
                DeviceId = deviceId,
                ReceivedAt = time.GetUtcNow().UtcDateTime,
            };
            db.Ops.Add(log);
            if (Hlc.TryParse(op.Hlc, out var hlc))
                clock.Receive(hlc);

            if (row is null)
            {
                row = new CurrentRow
                {
                    EntityType = op.EntityType,
                    EntityId = key.Item2,
                    UserId = userId,
                    WorkspaceId = op.WorkspaceId,
                };
                db.CurrentRows.Add(row);
                rows[key] = row;
            }
            Apply(op, row);
            accepted.Add(op.OpId);
            known.Add(op.OpId);
        }

        // Seq is assigned on insert; row.LastSeq needs it, so it is stamped after the first save.
        await db.SaveChangesAsync(ct);
        var touched = rows.Values.ToList();
        if (touched.Count > 0)
        {
            var acceptedSet = accepted.ToHashSet();
            var lastSeq = await db
                .Ops.Where(o => acceptedSet.Contains(o.OpId))
                .GroupBy(o => new { o.EntityType, o.EntityId })
                .Select(g => new
                {
                    g.Key.EntityType,
                    g.Key.EntityId,
                    Seq = g.Max(o => o.Seq),
                })
                .ToListAsync(ct);
            foreach (var s in lastSeq)
                if (rows.TryGetValue((s.EntityType, s.EntityId), out var r))
                    r.LastSeq = s.Seq;
            await db.SaveChangesAsync(ct);
        }
        return (accepted, rejected);
    }

    // Returns a rejection code, or null if the op is well-formed.
    static string? Validate(Op op, long maxHlcMs)
    {
        if (SyncRows.FieldsOf(op.EntityType) is not { } fields)
            return "UNKNOWN_ENTITY_TYPE";
        if (!Hlc.TryParse(op.Hlc, out var hlc))
            return "INVALID_HLC";
        if (hlc.Ms > maxHlcMs)
            return "HLC_TOO_FAR_AHEAD";
        if (op.EntityType == EntityTypes.Workspace && op.EntityId != op.WorkspaceId)
            return "INVALID_ENTITY";
        if ((op.Value?.ToJsonString().Length ?? 0) > MaxValueChars)
            return "VALUE_TOO_LARGE";

        switch (op.Kind)
        {
            case OpKinds.Set:
                if (EntityTypes.IsImmutable(op.EntityType))
                    return "IMMUTABLE_ENTITY";
                return op.Field is not null && fields.Contains(op.Field) ? null : "UNKNOWN_FIELD";
            case OpKinds.Insert:
                if (op.Value is not JsonObject obj)
                    return "INVALID_VALUE";
                return obj.All(kv => kv.Key == "id" || fields.Contains(kv.Key))
                    ? null
                    : "UNKNOWN_FIELD";
            default:
                return "UNKNOWN_KIND";
        }
    }

    static void Apply(Op op, CurrentRow row)
    {
        if (EntityTypes.IsImmutable(op.EntityType))
        {
            // Events are write-once; a second insert with the same id keeps the first.
            if (row.Row == "{}" && op.Value is JsonObject ev)
            {
                row.Row = ev.ToJsonString();
                row.RefId = ev["item_id"]?.GetValue<string>();
            }
            return;
        }

        var json = JsonNode.Parse(row.Row)!.AsObject();
        var clocks = JsonSerializer.Deserialize<Dictionary<string, string>>(row.FieldClocks)!;
        json["id"] = op.EntityId.ToString();
        LwwRow.Apply(op, json, clocks);

        row.Row = json.ToJsonString();
        row.FieldClocks = JsonSerializer.Serialize(clocks);
        row.DeletedAt = json["deleted_at"]?.GetValue<string>() is { } d
            ? DateTimeOffset.Parse(d).UtcDateTime
            : null;
    }

    async Task<(List<Op> Ops, long NextCursor, bool HasMore)> PullAsync(
        Guid userId,
        Guid deviceId,
        SyncRequest req,
        long maxSeq,
        CancellationToken ct
    )
    {
        var requested = req.Workspaces.ToList();
        var owned = await db
            .WorkspaceSyncs.Where(w => w.UserId == userId && requested.Contains(w.WorkspaceId))
            .Select(w => w.WorkspaceId)
            .ToListAsync(ct);

        var limit = Math.Clamp(req.Limit <= 0 ? 500 : req.Limit, 1, 500);
        var rows = await db
            .Ops.Where(o =>
                o.Seq > req.Cursor && o.DeviceId != deviceId && owned.Contains(o.WorkspaceId)
            )
            .OrderBy(o => o.Seq)
            .Take(limit + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        // With nothing left to send the cursor jumps to the head, so skipped ops (own, unlisted workspaces) aren't rescanned.
        var next = hasMore ? rows[^1].Seq : maxSeq;
        return (rows.Select(ToOp).ToList(), next, hasMore);
    }

    public static Op ToOp(OpRow r) =>
        new(
            r.OpId,
            r.EntityType,
            Guid.Parse(r.EntityId),
            r.WorkspaceId,
            r.Kind,
            r.Field,
            JsonNode.Parse(r.Value),
            r.Hlc,
            r.DeviceId
        )
        {
            Seq = r.Seq,
        };

    public async Task<SnapshotPage> SnapshotAsync(
        Guid userId,
        Guid workspaceId,
        string entityType,
        string? after,
        int limit,
        CancellationToken ct
    )
    {
        if (SyncRows.FieldsOf(entityType) is null)
            throw ApiException.BadRequest("UNKNOWN_ENTITY_TYPE", "Unknown entity type");
        await EnsureOwnerAsync(userId, workspaceId, ct);

        var seq = await db.Ops.MaxAsync(o => (long?)o.Seq, ct) ?? 0;
        limit = Math.Clamp(limit <= 0 ? 500 : limit, 1, 1000);

        var query = db.CurrentRows.Where(r =>
            r.UserId == userId && r.WorkspaceId == workspaceId && r.EntityType == entityType
        );
        if (after is not null)
            query = query.Where(r => string.Compare(r.EntityId, after) > 0);
        var page = await query.OrderBy(r => r.EntityId).Take(limit + 1).ToListAsync(ct);

        var more = page.Count > limit;
        if (more)
            page.RemoveAt(page.Count - 1);

        var rows = page.Select(r => new SnapshotRow(
                Guid.Parse(r.EntityId),
                JsonNode.Parse(r.Row)!.AsObject(),
                JsonSerializer.Deserialize<Dictionary<string, string>>(r.FieldClocks)!
            ))
            .ToList();
        return new SnapshotPage(seq, entityType, rows, more ? page[^1].EntityId : null);
    }

    public async Task<IReadOnlyList<Guid>> ListWorkspacesAsync(Guid userId, CancellationToken ct) =>
        await db
            .WorkspaceSyncs.Where(w => w.UserId == userId)
            .Select(w => w.WorkspaceId)
            .ToListAsync(ct);

    // Removes the server copy only; devices keep their local data.
    public async Task DeleteWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        await EnsureOwnerAsync(userId, workspaceId, ct);
        await IngestLock.WaitAsync(ct);
        try
        {
            await db.Ops.Where(o => o.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
            await db.CurrentRows.Where(r => r.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
            await db.WorkspaceSyncs.Where(w => w.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
        }
        finally
        {
            IngestLock.Release();
        }
    }

    async Task EnsureOwnerAsync(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var owner =
            await db.WorkspaceSyncs.FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId, ct)
            ?? throw ApiException.NotFound("WORKSPACE_NOT_FOUND", "Workspace not found on server");
        if (owner.UserId != userId)
            throw ApiException.Forbidden("WORKSPACE_NOT_OWNED", "Workspace not owned by user");
    }
}

// Server-side HLC so responses carry a clock devices can advance from.
public sealed class ServerClock(TimeProvider time)
{
    readonly HybridClock _clock = new(() => time.GetUtcNow().ToUnixTimeMilliseconds(), Guid.Empty);

    public void Receive(Hlc remote) => _clock.Receive(remote);

    public string Now() => _clock.Next().ToString();
}
