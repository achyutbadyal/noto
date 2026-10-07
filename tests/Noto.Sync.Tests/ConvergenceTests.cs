using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Sync;
using Noto.Sync.Tests.Support;

namespace Noto.Sync.Tests;

// docs/08 Phase 8 acceptance: 3 replicas, random ops, random partitions and clock skew must end with
// identical state everywhere, and no edit to a different field may be lost.
public sealed class ConvergenceTests
{
    [Fact]
    public async Task Three_replicas_converge_after_10k_random_commands_with_partitions_and_skew() =>
        await RunAsync(seed: 20261007, steps: 10_000);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Converges_for_other_seeds(int seed) => await RunAsync(seed, steps: 1_500);

    static async Task RunAsync(int seed, int steps)
    {
        var rng = new Random(seed);
        using var server = new TestServer();
        var user = server.RegisterUser();

        // Skews of -90s, 0 and +4min: devices disagree about the time throughout.
        var skews = new[] { TimeSpan.FromSeconds(-90), TimeSpan.Zero, TimeSpan.FromMinutes(4) };
        var replicas = skews
            .Select(s => new Replica(
                server,
                user,
                s,
                new SyncOptions { PullLimit = 200, PushBatch = 200 }
            ))
            .ToList();
        try
        {
            var ws = await replicas[0].AddWorkspaceAsync();
            await replicas[0].Workspaces.EnableAsync(ws.Id);
            await replicas[0].Client.SyncAsync();
            foreach (var r in replicas.Skip(1))
                await r.Workspaces.BootstrapNewDeviceAsync();

            var ids = new List<Guid>();
            var lastUndo = new Dictionary<Replica, Guid>();

            for (var step = 0; step < steps; step++)
            {
                server.Time.Advance(TimeSpan.FromMilliseconds(rng.Next(1, 3000)));
                var replica = replicas[rng.Next(replicas.Count)];

                await RandomCommandAsync(rng, replica, ws.Id, ids, lastUndo);

                if (step % 20 == 0) // partitions come and go
                    foreach (var r in replicas)
                        r.Transport.Online = rng.NextDouble() > 0.35;
                if (rng.NextDouble() < 0.08)
                {
                    var syncing = replicas[rng.Next(replicas.Count)];
                    if (syncing.Transport.Online)
                        await syncing.Client.SyncAsync();
                }
            }

            // Heal the network and let everyone drain until nothing is left to exchange.
            foreach (var r in replicas)
                r.Transport.Online = true;
            for (var round = 0; round < 6; round++)
                foreach (var r in replicas)
                    await r.Client.SyncAsync();
            foreach (var r in replicas)
                (await r.PendingAsync()).ShouldBeEmpty();

            await AssertConvergedAsync(replicas, ws.Id, server);

            // The run must actually have exercised concurrent text edits, or convergence proves little.
            var conflicts = 0;
            foreach (var id in ids)
                conflicts +=
                    (await replicas[0].Db.RunAsync(s => s.Sync.ListConflictsAsync(id))).Count
                    + (await replicas[1].Db.RunAsync(s => s.Sync.ListConflictsAsync(id))).Count
                    + (await replicas[2].Db.RunAsync(s => s.Sync.ListConflictsAsync(id))).Count;
            conflicts.ShouldBeGreaterThan(0);
        }
        finally
        {
            foreach (var r in replicas)
                r.Dispose();
        }
    }

    static async Task RandomCommandAsync(
        Random rng,
        Replica replica,
        Guid ws,
        List<Guid> ids,
        Dictionary<Replica, Guid> lastUndo
    )
    {
        ItemCommand? command = null;
        var roll = rng.Next(100);
        var existing = ids.Count == 0 ? Guid.Empty : ids[rng.Next(ids.Count)];

        if (roll < 25 || ids.Count == 0)
        {
            var id = Guid.CreateVersion7();
            ids.Add(id);
            command = new CreateItem(
                id,
                ws,
                $"task {rng.Next(1000)}",
                new DateOnly(2026, 10, 7).AddDays(rng.Next(-3, 4)),
                IsSomeday: rng.Next(10) == 0
            );
        }
        else
            command = roll switch
            {
                < 40 => new RenameItem(existing, $"title {rng.Next(100000)}"),
                < 50 => new SetPriority(existing, rng.Next(5)),
                < 60 => new PlanItem(
                    existing,
                    new DateOnly(2026, 10, 7).AddDays(rng.Next(-2, 6)),
                    PlanKind.Plan
                ),
                < 68 => new CompleteItem(existing),
                < 72 => new ReopenItem(existing),
                < 76 => new DropItem(existing, DropReason.NotNeeded),
                < 79 => new RestoreItem(existing),
                < 83 => new StartWaiting(existing, "someone"),
                < 86 => new EndWaiting(existing),
                < 89 => new SetSomeday(existing, rng.Next(2) == 0),
                < 92 => new SetEstimate(existing, rng.Next(1, 240)),
                < 95 => new SetNotes(existing, $"notes {rng.Next(100000)}"),
                < 97 => new SetDueDate(existing, new DateOnly(2026, 11, 1).AddDays(rng.Next(30))),
                < 98 => new DeleteItem(existing),
                _ => null,
            };

        try
        {
            if (command is null)
            {
                if (lastUndo.Remove(replica, out var token))
                    await replica.Bus.UndoAsync(token);
                return;
            }
            var result = await replica.Bus.SendAsync(command);
            lastUndo[replica] = result.UndoToken;
        }
        catch (Exception e) when (e is CommandException or InvariantViolationException)
        {
            // Item unknown to this replica yet, or the transition isn't valid from its state.
        }
    }

    static async Task AssertConvergedAsync(List<Replica> replicas, Guid ws, TestServer server)
    {
        var views = new List<Dictionary<Guid, (string Row, string Raw)>>();
        var events = new List<HashSet<Guid>>();
        var workspaces = new List<string>();

        foreach (var r in replicas)
        {
            var (items, raws, evs, wsRow) = await r.Db.RunAsync(async s =>
            {
                var all = await s.Items.ListAllAsync(ws);
                var raw = new Dictionary<Guid, string>();
                foreach (var i in all)
                    raw[i.Id] = (
                        await s.Sync.GetRawAsync(EntityTypes.TodoItem, i.Id) ?? new JsonObject()
                    ).ToJsonString();
                var ev = (await s.Events.ListForWorkspaceAsync(ws)).Select(e => e.Id).ToHashSet();
                var w = SyncRows.ToRow((await s.Workspaces.GetAsync(ws))!).ToJsonString();
                return (all, raw, ev, w);
            });

            views.Add(
                items.ToDictionary(
                    i => i.Id,
                    i => (SyncRows.ToRow(i).ToJsonString(), Sorted(raws[i.Id]))
                )
            );
            events.Add(evs);
            workspaces.Add(wsRow);
        }

        // 1. Identical state on every replica: typed rows, raw LWW rows, events and workspace.
        for (var n = 1; n < replicas.Count; n++)
        {
            views[n]
                .Keys.OrderBy(k => k)
                .ShouldBe(views[0].Keys.OrderBy(k => k), $"item sets differ on replica {n}");
            foreach (var (id, expected) in views[0])
            {
                views[n][id].Row.ShouldBe(expected.Row, $"item {id} differs on replica {n}");
                views[n][id].Raw.ShouldBe(expected.Raw, $"raw item {id} differs on replica {n}");
            }
            events[n].SetEquals(events[0]).ShouldBeTrue($"events differ on replica {n}");
            workspaces[n].ShouldBe(workspaces[0]);
        }

        // 2. Nothing lost: every field holds the value of the highest-HLC op ever accepted for it.
        await using var db = server.NewDb();
        var rejected = 0;
        var oracle =
            new Dictionary<(string Type, string Id, string Field), (string Hlc, string Value)>();
        foreach (
            var op in db
                .Ops.AsNoTracking()
                .Where(o => o.EntityType != EntityTypes.ItemEvent)
                .OrderBy(o => o.Seq)
                .ToList()
        )
        {
            var fields =
                op.Kind == OpKinds.Set
                    ? [(op.Field!, op.Value)]
                    : JsonNode
                        .Parse(op.Value)!
                        .AsObject()
                        .Where(kv => kv.Key != "id")
                        .Select(kv => (kv.Key, kv.Value?.ToJsonString() ?? "null"))
                        .ToList();
            foreach (var (field, value) in fields)
            {
                var key = (op.EntityType, op.EntityId, field);
                if (!oracle.TryGetValue(key, out var best) || Hlc.Compare(op.Hlc, best.Hlc) > 0)
                    oracle[key] = (op.Hlc, value);
            }
        }
        rejected.ShouldBe(0);
        oracle.Count.ShouldBeGreaterThan(100);

        foreach (var ((type, id, field), (hlc, value)) in oracle)
        {
            if (type != EntityTypes.TodoItem)
                continue;
            var raw = JsonNode.Parse(views[0][Guid.Parse(id)].Raw)!.AsObject();
            (raw[field]?.ToJsonString() ?? "null").ShouldBe(
                Normalize(value),
                $"{type}/{id}.{field} lost the winning op {hlc}"
            );
        }

        // 3. The server's materialized rows agree with the oracle too.
        foreach (
            var row in db
                .CurrentRows.AsNoTracking()
                .Where(r => r.EntityType == EntityTypes.TodoItem)
                .ToList()
        )
        {
            var json = JsonNode.Parse(row.Row)!.AsObject();
            foreach (var field in SyncRows.ItemFields)
                if (oracle.TryGetValue((EntityTypes.TodoItem, row.EntityId, field), out var best))
                    (json[field]?.ToJsonString() ?? "null").ShouldBe(Normalize(best.Value));
        }

        // Every typed row is a valid item after repair.
        foreach (var (id, view) in views[0])
            ItemInvariants
                .Check(SyncRows.ToItem(JsonNode.Parse(view.Row)!.AsObject()))
                .ShouldBeEmpty($"invariants broken for {id}");
    }

    static string Normalize(string json) => JsonNode.Parse(json)?.ToJsonString() ?? "null";

    static string Sorted(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        var sorted = new JsonObject();
        foreach (var kv in o.OrderBy(k => k.Key, StringComparer.Ordinal))
            sorted[kv.Key] = kv.Value?.DeepClone();
        return sorted.ToJsonString();
    }
}
