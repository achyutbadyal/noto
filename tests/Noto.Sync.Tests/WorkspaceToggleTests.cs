using Microsoft.EntityFrameworkCore;
using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Sync;
using Noto.Server.Sync;
using Noto.Sync.Tests.Support;

namespace Noto.Sync.Tests;

public sealed class WorkspaceToggleTests : IDisposable
{
    readonly TestServer _server = new();
    readonly Guid _user;
    readonly Replica _a,
        _b;

    public WorkspaceToggleTests()
    {
        _user = _server.RegisterUser();
        _a = new Replica(_server, _user, TimeSpan.Zero);
        _b = new Replica(_server, _user, TimeSpan.Zero);
    }

    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task Enabling_pushes_the_whole_workspace_including_history_and_tombstones()
    {
        var ws = await _a.AddWorkspaceAsync();
        var kept = await _a.AddItemAsync(ws.Id, "kept");
        var gone = await _a.AddItemAsync(ws.Id, "gone");
        await _a.Bus.SendAsync(new DeleteItem(gone));
        await _a.Bus.SendAsync(new CompleteItem(kept));

        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();
        await _b.Workspaces.BootstrapNewDeviceAsync();

        (await _b.ItemAsync(kept))!.Status.ShouldBe(ItemStatus.Done);
        (await _b.ItemAsync(gone))!.DeletedAt.ShouldNotBeNull();
        (await _b.Db.RunAsync(s => s.Events.ListForItemAsync(kept)))
            .Select(e => e.Type)
            .ShouldBe([ItemEventType.Created, ItemEventType.Completed]);
    }

    [Fact]
    public async Task Enabling_keeps_original_field_clocks_so_old_edits_cannot_beat_newer_server_data()
    {
        var ws = await _a.AddWorkspaceAsync();
        var item = await _a.AddItemAsync(ws.Id, "old title");
        var clockBefore = (
            await _a.Db.RunAsync(s => s.Sync.GetClocksAsync(EntityTypes.TodoItem, item))
        )["title"];
        await _a.Workspaces.EnableAsync(ws.Id);

        var pending = await _a.PendingAsync();

        pending.Single(o => o.EntityId == item && o.Field == "title").Hlc.ShouldBe(clockBefore);
    }

    [Fact]
    public async Task Disabling_stops_pushing_and_pulling_but_keeps_local_data()
    {
        var ws = await _a.AddWorkspaceAsync();
        var item = await _a.AddItemAsync(ws.Id);
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();
        await _b.Workspaces.BootstrapNewDeviceAsync();

        await _a.Workspaces.DisableAsync(ws.Id, removeFromServer: false);
        await _a.Bus.SendAsync(new RenameItem(item, "offline edit"));
        await _b.Bus.SendAsync(new RenameItem(item, "from b"));
        await _b.Client.SyncAsync();
        await _a.Client.SyncAsync();

        (await _a.PendingAsync()).ShouldBeEmpty();
        (await _a.ItemAsync(item))!.Title.ShouldBe("offline edit"); // no pull for a disabled workspace
        (await _b.ItemAsync(item))!.Title.ShouldBe("from b");
        await using var db = _server.NewDb();
        db.WorkspaceSyncs.Count().ShouldBe(1); // server copy stays
    }

    [Fact]
    public async Task Re_enabling_merges_per_field_instead_of_overwriting_either_side()
    {
        var ws = await _a.AddWorkspaceAsync();
        var item = await _a.AddItemAsync(ws.Id, "original");
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();
        await _b.Workspaces.BootstrapNewDeviceAsync();

        await _a.Workspaces.DisableAsync(ws.Id, removeFromServer: false);
        await _a.Bus.SendAsync(new RenameItem(item, "A renamed while off"));
        _server.Time.Advance(TimeSpan.FromMinutes(1));
        await _b.Bus.SendAsync(new CompleteItem(item)); // different field, on the other side
        await _b.Client.SyncAsync();

        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();
        await _b.Client.SyncAsync();

        foreach (var r in new[] { _a, _b })
        {
            var i = (await r.ItemAsync(item))!;
            i.Title.ShouldBe("A renamed while off");
            i.Status.ShouldBe(ItemStatus.Done);
        }
    }

    [Fact]
    public async Task Remove_from_server_deletes_only_the_server_copy()
    {
        var ws = await _a.AddWorkspaceAsync();
        var item = await _a.AddItemAsync(ws.Id);
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();

        await _a.Workspaces.DisableAsync(ws.Id, removeFromServer: true);

        await using var db = _server.NewDb();
        db.WorkspaceSyncs.Count().ShouldBe(0);
        db.Ops.Count().ShouldBe(0);
        db.CurrentRows.Count().ShouldBe(0);
        (await _a.ItemAsync(item)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Snapshot_is_paginated_by_entity_type()
    {
        var small = new SyncOptions { SnapshotPage = 3 };
        using var c = new Replica(_server, _user, TimeSpan.Zero, small);
        var ws = await _a.AddWorkspaceAsync();
        for (var n = 0; n < 8; n++)
            await _a.AddItemAsync(ws.Id, $"item {n}");
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();

        await c.Workspaces.BootstrapNewDeviceAsync();

        (await c.Db.RunAsync(s => s.Items.ListAsync(ws.Id))).Count.ShouldBe(8);
        (await c.Db.RunAsync(s => s.Events.ListForWorkspaceAsync(ws.Id))).Count.ShouldBe(8);
    }

    [Fact]
    public async Task Server_snapshot_exposes_rows_with_their_field_clocks()
    {
        var ws = await _a.AddWorkspaceAsync();
        var item = await _a.AddItemAsync(ws.Id, "x");
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();

        var page = await _a.Transport.SnapshotAsync(ws.Id, EntityTypes.TodoItem, null, 10);

        var row = page.Rows.Single();
        row.Id.ShouldBe(item);
        row.Row["title"]!.GetValue<string>().ShouldBe("x");
        row.FieldClocks.ShouldContainKey("title");
        page.Seq.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Tombstone_gc_waits_for_every_device_and_for_the_retention_period()
    {
        var ws = await _a.AddWorkspaceAsync();
        var item = await _a.AddItemAsync(ws.Id);
        await _a.Bus.SendAsync(new DeleteItem(item));
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();
        // B exists but has not pulled yet: its cursor is 0.

        async Task<int> Gc()
        {
            await using var db = _server.NewDb();
            return await new TombstoneGc(db, _server.Time).RunAsync();
        }

        _server.Time.Advance(TimeSpan.FromDays(91));
        (await Gc()).ShouldBe(0); // B has not pulled past the delete

        await _b.Workspaces.BootstrapNewDeviceAsync();
        await _b.Client.SyncAsync();
        await _a.Client.SyncAsync();
        (await Gc()).ShouldBe(1);

        await using var db = _server.NewDb();
        db.CurrentRows.Any(r => r.EntityId == item.ToString()).ShouldBeFalse();
        db.CurrentRows.Count(r => r.EntityType == EntityTypes.ItemEvent).ShouldBe(0);
    }

    [Fact]
    public async Task Tombstone_gc_keeps_recent_tombstones()
    {
        var ws = await _a.AddWorkspaceAsync();
        var item = await _a.AddItemAsync(ws.Id);
        await _a.Bus.SendAsync(new DeleteItem(item));
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();
        await _a.Client.SyncAsync();

        _server.Time.Advance(TimeSpan.FromDays(30));

        await using var db = _server.NewDb();
        (await new TombstoneGc(db, _server.Time).RunAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Local_purge_removes_old_tombstones_with_their_history()
    {
        var ws = await _a.AddWorkspaceAsync();
        var item = await _a.AddItemAsync(ws.Id);
        await _a.Bus.SendAsync(new DeleteItem(item));

        var purged = await _a.Db.RunAsync(s =>
            s.Sync.PurgeTombstonedItemsAsync(ws.Id, DateTimeOffset.UtcNow.AddYears(10))
        );

        purged.ShouldBe(1);
        (await _a.ItemAsync(item)).ShouldBeNull();
        (await _a.Db.RunAsync(s => s.Events.ListForItemAsync(item))).ShouldBeEmpty();
    }
}
