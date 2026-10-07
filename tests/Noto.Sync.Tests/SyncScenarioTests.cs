using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Sync;
using Noto.Sync.Tests.Support;

namespace Noto.Sync.Tests;

public sealed class SyncScenarioTests : IDisposable
{
    readonly TestServer _server = new();
    readonly Guid _user;
    readonly Replica _a, _b;
    Guid _ws;

    public SyncScenarioTests()
    {
        _user = _server.RegisterUser();
        _a = new Replica(_server, _user, TimeSpan.Zero);
        _b = new Replica(_server, _user, TimeSpan.FromSeconds(3)); // B's clock runs 3s ahead
    }

    public void Dispose() { _a.Dispose(); _b.Dispose(); _server.Dispose(); }

    // A owns a synced workspace with one item; B bootstraps from the server.
    async Task<Guid> SharedItemAsync(string title = "Task")
    {
        var ws = await _a.AddWorkspaceAsync();
        _ws = ws.Id;
        var item = await _a.AddItemAsync(ws.Id, title);
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();
        await _b.Workspaces.BootstrapNewDeviceAsync();
        return item;
    }

    async Task SyncBothAsync()
    {
        for (var i = 0; i < 3; i++) { await _a.Client.SyncAsync(); await _b.Client.SyncAsync(); }
    }

    [Fact]
    public async Task New_device_bootstrap_gets_the_workspace_items_and_events()
    {
        var item = await SharedItemAsync("Hello");

        (await _b.ItemAsync(item))!.Title.ShouldBe("Hello");
        var events = await _b.Db.RunAsync(s => s.Events.ListForItemAsync(item));
        events.Single().Type.ShouldBe(ItemEventType.Created);
        var ws = await _b.Db.RunAsync(s => s.Workspaces.GetAsync(_ws));
        ws!.SyncEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Bootstrap_reproduces_rows_exactly_including_immutable_creation_fields()
    {
        var item = await SharedItemAsync("Exact");

        var a = SyncRows.ToRow((await _a.ItemAsync(item))!).ToJsonString();
        var b = SyncRows.ToRow((await _b.ItemAsync(item))!).ToJsonString();
        b.ShouldBe(a);

        var wsA = SyncRows.ToRow((await _a.Db.RunAsync(s => s.Workspaces.GetAsync(_ws)))!).ToJsonString();
        var wsB = SyncRows.ToRow((await _b.Db.RunAsync(s => s.Workspaces.GetAsync(_ws)))!).ToJsonString();
        wsB.ShouldBe(wsA);
    }

    [Fact]
    public async Task Completing_on_one_device_and_renaming_on_another_both_survive()
    {
        var item = await SharedItemAsync();

        await _a.Bus.SendAsync(new CompleteItem(item));
        await _b.Bus.SendAsync(new RenameItem(item, "Renamed on B"));
        await SyncBothAsync();

        foreach (var replica in new[] { _a, _b })
        {
            var i = (await replica.ItemAsync(item))!;
            i.Status.ShouldBe(ItemStatus.Done);
            i.Title.ShouldBe("Renamed on B");
        }
    }

    [Fact]
    public async Task Concurrent_text_edits_converge_and_the_loser_is_kept_in_the_conflict_log()
    {
        var item = await SharedItemAsync();

        await _a.Bus.SendAsync(new RenameItem(item, "A's title"));
        await _b.Bus.SendAsync(new RenameItem(item, "B's title"));
        await SyncBothAsync();

        (await _a.ItemAsync(item))!.Title.ShouldBe("B's title"); // B's clock is ahead, so its edit is later
        (await _b.ItemAsync(item))!.Title.ShouldBe("B's title");

        // B pulled A's older edit after making its own: the losing value is kept for "Edited on 2 devices".
        var conflicts = await _b.Db.RunAsync(s => s.Sync.ListConflictsAsync(item));
        conflicts.ShouldContain(c => c.Field == "title" && c.LosingValue == "A's title");
    }

    [Fact]
    public async Task A_local_edit_that_loses_before_it_was_pushed_is_kept_too()
    {
        var item = await SharedItemAsync();

        await _a.Bus.SendAsync(new RenameItem(item, "A's unpushed title"));
        await _b.Bus.SendAsync(new RenameItem(item, "B's title"));
        await _b.Client.SyncAsync();
        await _a.Client.SyncAsync(); // A still has its edit queued when B's winning edit arrives

        (await _a.ItemAsync(item))!.Title.ShouldBe("B's title");
        var conflicts = await _a.Db.RunAsync(s => s.Sync.ListConflictsAsync(item));
        conflicts.ShouldContain(c => c.Field == "title" && c.LosingValue == "A's unpushed title");
    }

    [Fact]
    public async Task Notes_conflicts_are_logged_and_non_text_fields_are_not()
    {
        var item = await SharedItemAsync();

        await _a.Bus.SendAsync(new SetNotes(item, "A notes"));
        await _a.Bus.SendAsync(new SetPriority(item, 1));
        await _b.Bus.SendAsync(new SetNotes(item, "B notes"));
        await _b.Bus.SendAsync(new SetPriority(item, 3));
        await SyncBothAsync();

        var fields = (await _b.Db.RunAsync(s => s.Sync.ListConflictsAsync(item))).Select(c => c.Field).Distinct().ToList();
        fields.ShouldBe(["notes"]);
    }

    [Fact]
    public async Task A_remote_edit_that_loses_is_logged_on_the_device_that_kept_its_value()
    {
        var item = await SharedItemAsync();

        await _b.Bus.SendAsync(new RenameItem(item, "B's title"));
        _server.Time.Advance(TimeSpan.FromMinutes(10)); // A's edit is then clearly newer than B's skewed clock
        await _a.Bus.SendAsync(new RenameItem(item, "A's final title"));
        await _a.Client.SyncAsync();
        await _b.Client.SyncAsync();
        await _a.Client.SyncAsync();

        (await _b.ItemAsync(item))!.Title.ShouldBe("A's final title");
        var conflicts = await _b.Db.RunAsync(s => s.Sync.ListConflictsAsync(item));
        conflicts.ShouldContain(c => c.LosingValue == "B's title");
    }

    [Fact]
    public async Task Sequential_edits_do_not_create_spurious_conflicts()
    {
        var item = await SharedItemAsync();

        await _a.Bus.SendAsync(new RenameItem(item, "first"));
        await SyncBothAsync();
        _server.Time.Advance(TimeSpan.FromSeconds(10));
        await _b.Bus.SendAsync(new RenameItem(item, "second"));
        await SyncBothAsync();

        (await _a.ItemAsync(item))!.Title.ShouldBe("second");
        (await _a.Db.RunAsync(s => s.Sync.ListConflictsAsync(item))).ShouldBeEmpty();
        (await _b.Db.RunAsync(s => s.Sync.ListConflictsAsync(item))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_done_and_dropped_converge_to_a_valid_item_on_both_devices()
    {
        var item = await SharedItemAsync();

        await _a.Bus.SendAsync(new CompleteItem(item));
        await _b.Bus.SendAsync(new DropItem(item, DropReason.NotNeeded));
        await SyncBothAsync();

        var a = (await _a.ItemAsync(item))!;
        var b = (await _b.ItemAsync(item))!;
        ItemInvariants.Check(a).ShouldBeEmpty();
        a.Status.ShouldBe(b.Status);
        a.CompletedAt.ShouldBe(b.CompletedAt);
        a.DroppedAt.ShouldBe(b.DroppedAt);
    }

    [Fact]
    public async Task Events_are_idempotent_and_arrive_on_the_other_device()
    {
        var item = await SharedItemAsync();
        await _a.Bus.SendAsync(new CompleteItem(item));

        await SyncBothAsync();
        await SyncBothAsync(); // replays must not duplicate

        var events = await _b.Db.RunAsync(s => s.Events.ListForItemAsync(item));
        events.Select(e => e.Type).ShouldBe([ItemEventType.Created, ItemEventType.Completed]);
    }

    [Fact]
    public async Task Pull_is_paginated_with_a_seq_cursor()
    {
        var small = new SyncOptions { PullLimit = 5, PushBatch = 5 };
        using var c = new Replica(_server, _user, TimeSpan.Zero, small);
        await SharedItemAsync();
        for (var n = 0; n < 12; n++) await _a.AddItemAsync(_ws, $"item {n}");
        await _a.Client.SyncAsync();

        await c.Workspaces.BootstrapNewDeviceAsync();
        await c.Client.SyncAsync();

        (await c.Db.RunAsync(s => s.Items.ListAsync(_ws))).Count.ShouldBe(13);
        var cursor = await c.Db.RunAsync(s => s.Sync.GetStateAsync(SyncClient.CursorKey));
        long.Parse(cursor!).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Push_is_chunked_and_drains_the_queue()
    {
        var small = new SyncOptions { PushBatch = 7, PullLimit = 7 };
        using var c = new Replica(_server, _user, TimeSpan.Zero, small);
        var ws = await c.AddWorkspaceAsync();
        await c.Workspaces.EnableAsync(ws.Id);
        for (var n = 0; n < 10; n++) await c.AddItemAsync(ws.Id, $"item {n}");

        var result = await c.Client.SyncAsync();

        (await c.PendingAsync()).ShouldBeEmpty();
        result.Pushed.ShouldBeGreaterThan(20);
    }

    [Fact]
    public async Task Push_retries_are_idempotent()
    {
        var ws = await _a.AddWorkspaceAsync();
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.AddItemAsync(ws.Id);
        var ops = await _a.PendingAsync();

        var first = await _a.Transport.SyncAsync(new SyncRequest(_a.DeviceId, 0, [ws.Id], 500, ops));
        var second = await _a.Transport.SyncAsync(new SyncRequest(_a.DeviceId, 0, [ws.Id], 500, ops));

        second.AcceptedOpIds.ShouldBe(first.AcceptedOpIds);
        await using var db = _server.NewDb();
        db.Ops.Count().ShouldBe(ops.Count);
    }

    [Fact]
    public async Task Cursor_ahead_of_the_server_triggers_a_rebootstrap()
    {
        var item = await SharedItemAsync();
        await _b.Db.RunAsync(async s => { await s.Sync.SetStateAsync(SyncClient.CursorKey, "999999"); return 0; });
        await _a.Bus.SendAsync(new RenameItem(item, "after restore"));
        await _a.Client.SyncAsync();

        var result = await _b.Client.SyncAsync();

        result.Rebootstrapped.ShouldBeTrue();
        (await _b.ItemAsync(item))!.Title.ShouldBe("after restore");
    }

    [Fact]
    public async Task Another_users_workspace_is_rejected_per_op()
    {
        await SharedItemAsync();
        var other = _server.RegisterUser();
        using var intruder = new Replica(_server, other, TimeSpan.Zero);
        var op = new Op(Guid.NewGuid(), EntityTypes.Workspace, _ws, _ws, OpKinds.Set, "name", "hijacked",
            new Hlc(long.Parse(_server.Time.GetUtcNow().ToUnixTimeMilliseconds().ToString()) + 100, 0, intruder.DeviceId).ToString(), intruder.DeviceId);

        var response = await intruder.Transport.SyncAsync(new SyncRequest(intruder.DeviceId, 0, [], 500, [op]));

        response.Rejected.Single().Code.ShouldBe("WORKSPACE_NOT_OWNED");
        var ws = await _a.Db.RunAsync(s => s.Workspaces.GetAsync(_ws));
        ws!.Name.ShouldBe("Work");
    }

    [Fact]
    public async Task Ops_with_absurdly_future_clocks_are_rejected()
    {
        await SharedItemAsync();
        var future = new Hlc(_server.Time.GetUtcNow().AddDays(30).ToUnixTimeMilliseconds(), 0, _a.DeviceId).ToString();
        var op = new Op(Guid.NewGuid(), EntityTypes.Workspace, _ws, _ws, OpKinds.Set, "name", "x", future, _a.DeviceId);

        var response = await _a.Transport.SyncAsync(new SyncRequest(_a.DeviceId, 0, [], 500, [op]));

        response.Rejected.Single().Code.ShouldBe("HLC_TOO_FAR_AHEAD");
    }

    [Fact]
    public async Task Pushing_more_than_2000_ops_is_refused()
    {
        var ops = Enumerable.Range(0, 2001).Select(_ => new Op(Guid.NewGuid(), EntityTypes.Workspace, _ws, _ws, OpKinds.Set, "name", "x", new Hlc(1, 0, _a.DeviceId).ToString(), _a.DeviceId)).ToList();
        var ex = await Should.ThrowAsync<Noto.Server.Middleware.ApiException>(() =>
            _server.WithServiceAsync(s => s.SyncAsync(_user, _a.DeviceId, new SyncRequest(_a.DeviceId, 0, [], 500, ops), default)));
        ex.Status.ShouldBe(413);
    }

    [Fact]
    public async Task Own_ops_are_not_echoed_back()
    {
        var item = await SharedItemAsync();
        await _a.Bus.SendAsync(new RenameItem(item, "mine"));

        var result = await _a.Client.SyncAsync();

        result.Pulled.ShouldBe(0);
    }
}
