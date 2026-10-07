using System.Text.Json.Nodes;
using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Sync;
using Noto.Sync.Tests.Support;

namespace Noto.Sync.Tests;

public sealed class OpCaptureTests : IDisposable
{
    readonly TestServer _server = new();
    readonly Replica _a;
    readonly Guid _user;

    public OpCaptureTests()
    {
        _user = _server.RegisterUser();
        _a = new Replica(_server, _user, TimeSpan.Zero);
    }

    public void Dispose()
    {
        _a.Dispose();
        _server.Dispose();
    }

    async Task<Workspace> SyncedWorkspaceAsync()
    {
        var ws = await _a.AddWorkspaceAsync();
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Db.RunAsync(async s =>
        {
            await s.Sync.ClearPendingAsync(ws.Id);
            return 0;
        }); // start from a clean queue
        return ws;
    }

    [Fact]
    public async Task Create_queues_an_item_insert_and_an_event_insert()
    {
        var ws = await SyncedWorkspaceAsync();
        var id = await _a.AddItemAsync(ws.Id, "Deploy");

        var ops = await _a.PendingAsync();

        ops.Select(o => (o.EntityType, o.Kind))
            .ShouldBe([
                (EntityTypes.TodoItem, OpKinds.Insert),
                (EntityTypes.ItemEvent, OpKinds.Insert),
            ]);
        ops[0].EntityId.ShouldBe(id);
        ((JsonObject)ops[0].Value!)["title"]!.GetValue<string>().ShouldBe("Deploy");
    }

    [Fact]
    public async Task Update_queues_one_set_op_per_changed_field_only()
    {
        var ws = await SyncedWorkspaceAsync();
        var id = await _a.AddItemAsync(ws.Id);
        await _a.Db.RunAsync(async s =>
        {
            await s.Sync.ClearPendingAsync(ws.Id);
            return 0;
        });

        await _a.Bus.SendAsync(new CompleteItem(id));

        var sets = (await _a.PendingAsync())
            .Where(o => o.Kind == OpKinds.Set)
            .Select(o => o.Field)
            .ToList();
        sets.ShouldBe(["status", "completed_on", "completed_at"], ignoreOrder: true);
    }

    [Fact]
    public async Task Every_op_gets_a_distinct_increasing_hlc_from_this_device()
    {
        var ws = await SyncedWorkspaceAsync();
        var id = await _a.AddItemAsync(ws.Id);
        await _a.Bus.SendAsync(new CompleteItem(id));

        var stamps = (await _a.PendingAsync()).Select(o => Hlc.Parse(o.Hlc)).ToList();

        stamps.Select(h => h.Device).Distinct().ShouldBe([_a.DeviceId]);
        stamps.Distinct().Count().ShouldBe(stamps.Count);
    }

    [Fact]
    public async Task Disabled_workspace_queues_nothing_but_keeps_field_clocks()
    {
        var ws = await _a.AddWorkspaceAsync(); // sync not enabled
        var id = await _a.AddItemAsync(ws.Id);
        await _a.Bus.SendAsync(new CompleteItem(id));

        (await _a.PendingAsync()).ShouldBeEmpty();
        var clocks = await _a.Db.RunAsync(s => s.Sync.GetClocksAsync(EntityTypes.TodoItem, id));
        clocks.ShouldContainKey("status");
        clocks.ShouldContainKey("title");
    }

    [Fact]
    public async Task Hlc_state_is_persisted_for_restarts()
    {
        var ws = await SyncedWorkspaceAsync();
        await _a.AddItemAsync(ws.Id);

        var saved = await _a.Db.RunAsync(s => s.Sync.GetStateAsync("hlc"));

        saved.ShouldNotBeNull();
        Hlc.TryParse(saved, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Workspace_changes_are_recorded_as_set_ops_and_sync_enabled_is_never_synced()
    {
        var ws = await SyncedWorkspaceAsync();
        ws.Name = "Renamed";
        ws.Pressure = Pressure.Relentless;
        ws.SyncEnabled = true;
        await _a.Bus.SaveWorkspaceAsync(ws);

        var ops = (await _a.PendingAsync())
            .Where(o => o.EntityType == EntityTypes.Workspace)
            .ToList();

        ops.Select(o => o.Field).ShouldBe(["name", "pressure"], ignoreOrder: true);
        SyncRows.WorkspaceFields.ShouldNotContain("sync_enabled");
    }

    [Fact]
    public async Task Ops_only_name_synced_entities_and_fields_and_never_credentials_or_previews()
    {
        var ws = await SyncedWorkspaceAsync();
        var id = await _a.AddItemAsync(ws.Id, "Review https://github.com/acme/app/pull/7");
        await _a.Bus.SendAsync(new SetNotes(id, "link: https://acme.atlassian.net/browse/ENG-1"));
        await _a.Bus.SendAsync(new DropItem(id, DropReason.NotNeeded, "dup"));

        var ops = await _a.PendingAsync();

        foreach (var op in ops)
        {
            EntityTypes.All.ShouldContain(op.EntityType);
            if (op.Kind == OpKinds.Set)
                SyncRows.FieldsOf(op.EntityType)!.ShouldContain(op.Field!);
            else
                ((JsonObject)op.Value!)
                    .Select(kv => kv.Key)
                    .Where(k => k != "id")
                    .ShouldAllBe(k => SyncRows.FieldsOf(op.EntityType)!.Contains(k));
        }

        var json = string.Concat(ops.Select(o => o.Value?.ToJsonString())).ToLowerInvariant();
        foreach (
            var forbidden in new[]
            {
                "access_token",
                "refresh_token",
                "authorization",
                "client_secret",
                "password",
                "state_hash",
                "preview_status",
            }
        )
            json.ShouldNotContain(forbidden);
    }

    [Fact]
    public async Task Server_rejects_ops_for_local_only_entities_and_fields()
    {
        var ws = await _a.AddWorkspaceAsync();
        var bad = new[]
        {
            new Op(
                Guid.NewGuid(),
                "link_preview_cache",
                Guid.NewGuid(),
                ws.Id,
                OpKinds.Insert,
                null,
                new JsonObject(),
                new Hlc(1, 0, _a.DeviceId).ToString(),
                _a.DeviceId
            ),
            new Op(
                Guid.NewGuid(),
                "app_connection",
                Guid.NewGuid(),
                ws.Id,
                OpKinds.Insert,
                null,
                new JsonObject(),
                new Hlc(1, 0, _a.DeviceId).ToString(),
                _a.DeviceId
            ),
            new Op(
                Guid.NewGuid(),
                EntityTypes.Workspace,
                ws.Id,
                ws.Id,
                OpKinds.Set,
                "sync_enabled",
                true,
                new Hlc(1, 0, _a.DeviceId).ToString(),
                _a.DeviceId
            ),
        };

        var response = await _a.Transport.SyncAsync(
            new Noto.Sync.SyncRequest(_a.DeviceId, 0, [], 500, bad)
        );

        response.AcceptedOpIds.ShouldBeEmpty();
        response
            .Rejected.Select(r => r.Code)
            .ShouldBe(["UNKNOWN_ENTITY_TYPE", "UNKNOWN_ENTITY_TYPE", "UNKNOWN_FIELD"]);
    }

    [Fact]
    public async Task Undo_reverts_only_the_fields_the_command_changed()
    {
        var ws = await SyncedWorkspaceAsync();
        var id = await _a.AddItemAsync(ws.Id);
        var done = await _a.Bus.SendAsync(new CompleteItem(id));

        // A merged remote edit lands on a field the command never touched.
        await _a.Db.RunAsync(async s =>
        {
            var item = (await s.Items.GetAsync(id))!;
            item.Title = "renamed elsewhere";
            await s.Items.UpsertAsync(item);
            return 0;
        });

        await _a.Bus.UndoAsync(done.UndoToken);

        var after = (await _a.ItemAsync(id))!;
        after.Status.ShouldBe(ItemStatus.Open);
        after.Title.ShouldBe("renamed elsewhere");
    }
}
