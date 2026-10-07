using System.Text.Json.Nodes;
using Noto.Core.Commands;
using Noto.Core.Links;
using Noto.Core.Models;
using Noto.Core.Presets;
using Noto.Core.Recurrence;
using Noto.Core.Sync;
using Noto.Core.Workspaces;
using Noto.Sync.Tests.Support;

namespace Noto.Sync.Tests;

// Changes made by Core services (not through the CommandBus) must reach the op log and sync like any other.
public sealed class ServiceCaptureTests : IDisposable
{
    readonly TestServer _server = new();
    readonly Replica _a, _b;
    readonly Noto.Core.Time.IClock _clock;

    public ServiceCaptureTests()
    {
        var user = _server.RegisterUser();
        _a = new Replica(_server, user, TimeSpan.Zero);
        _b = new Replica(_server, user, TimeSpan.Zero);
        _clock = new SkewedClock(_server.Time, TimeSpan.Zero);
    }

    public void Dispose() { _a.Dispose(); _b.Dispose(); _server.Dispose(); }

    async Task<Guid> SyncedWorkspaceAsync()
    {
        var ws = await new WorkspaceService(_a.Db, _clock).CreateAsync("Work", "work", "#00f", BuiltInPresets.Sprint);
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();
        await _b.Workspaces.BootstrapNewDeviceAsync();
        return ws.Id;
    }

    async Task<IReadOnlyList<Op>> TakePendingAsync(Guid ws)
    {
        var ops = await _a.PendingAsync();
        await _a.Db.RunAsync(async s => { await s.Sync.ClearPendingAsync(ws); return 0; });
        return ops;
    }

    [Fact]
    public async Task Workspace_service_edits_are_recorded_and_reach_the_other_device()
    {
        var ws = await SyncedWorkspaceAsync();
        var service = new WorkspaceService(_a.Db, _clock);

        await service.RenameAsync(ws, "Renamed");
        await service.ApplyPresetAsync(ws, BuiltInPresets.Accountability);
        await service.SetCapacityAsync(ws, CapacityUnit.Items, 8);

        var ops = await _a.PendingAsync();
        ops.Where(o => o.EntityType == EntityTypes.Workspace).Select(o => o.Field)
            .ShouldBe(["name", "preset", "sort_order_mode", "pressure", "capacity_unit", "daily_capacity"], ignoreOrder: true);

        await _a.Client.SyncAsync();
        await _b.Client.SyncAsync();
        var onB = (await _b.Db.RunAsync(s => s.Workspaces.GetAsync(ws)))!;
        (onB.Name, onB.Pressure, onB.CapacityUnit, onB.DailyCapacity).ShouldBe(("Renamed", Pressure.Relentless, CapacityUnit.Items, 8));
    }

    [Fact]
    public async Task Archive_and_delete_flow_through_as_synced_fields()
    {
        var ws = await SyncedWorkspaceAsync();
        var service = new WorkspaceService(_a.Db, _clock);

        await service.ArchiveAsync(ws);
        await _a.Client.SyncAsync();
        await _b.Client.SyncAsync();

        (await _b.Db.RunAsync(s => s.Workspaces.GetAsync(ws)))!.ArchivedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Recurrence_rules_and_generated_instances_are_recorded_and_sync()
    {
        var ws = await SyncedWorkspaceAsync();
        var recurrence = new RecurrenceService(_a.Db, _clock);

        var rule = await recurrence.CreateRuleAsync(ws, "FREQ=DAILY", new RuleTemplate("Standup"), new DateOnly(2026, 10, 1), MissedBehavior.Carry);
        var generated = await recurrence.GenerateDueAsync(ws);

        generated.ShouldNotBeEmpty();
        var ops = await _a.PendingAsync();
        ops.ShouldContain(o => o.EntityType == EntityTypes.RecurrenceRule && o.EntityId == rule.Id && o.Kind == OpKinds.Insert);
        ops.ShouldContain(o => o.EntityType == EntityTypes.TodoItem && o.EntityId == generated[0].Id); // written by the service, not the bus

        await recurrence.UpdateAsync(rule.Id, r => r.RRule = "FREQ=WEEKLY;BYDAY=MO");
        (await _a.PendingAsync()).ShouldContain(o => o.EntityType == EntityTypes.RecurrenceRule && o.Field == "rrule");

        await _a.Client.SyncAsync();
        await _b.Client.SyncAsync();
        var onB = (await _b.Db.RunAsync(s => s.Rules.GetAsync(rule.Id)))!;
        onB.RRule.ShouldBe("FREQ=WEEKLY;BYDAY=MO");
        onB.Template.Title.ShouldBe("Standup");
        (await _b.ItemAsync(generated[0].Id))!.Title.ShouldBe("Standup");
    }

    [Fact]
    public async Task Concurrently_generated_instances_collapse_to_one_row()
    {
        var ws = await SyncedWorkspaceAsync();
        var rule = await new RecurrenceService(_a.Db, _clock).CreateRuleAsync(ws, "FREQ=DAILY", new RuleTemplate("Daily"), new DateOnly(2026, 10, 7));
        await _a.Client.SyncAsync();
        await _b.Client.SyncAsync();

        var onA = await new RecurrenceService(_a.Db, _clock).GenerateDueAsync(ws);
        var onB = await new RecurrenceService(_b.Db, new SkewedClock(_server.Time, TimeSpan.Zero)).GenerateDueAsync(ws);
        for (var i = 0; i < 3; i++) { await _a.Client.SyncAsync(); await _b.Client.SyncAsync(); }

        onA.Select(i => i.Id).ShouldBe(onB.Select(i => i.Id)); // deterministic ids
        (await _a.Db.RunAsync(s => s.Items.ListAsync(ws))).Count.ShouldBe(onA.Count);
        (await _b.Db.RunAsync(s => s.Items.ListAsync(ws))).Count.ShouldBe(onA.Count);
        rule.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Day_notes_are_recorded_and_text_conflicts_are_kept()
    {
        var ws = await SyncedWorkspaceAsync();
        var a = new DayNoteService(_a.Db);
        var b = new DayNoteService(_b.Db);
        var day = new DateOnly(2026, 10, 7);

        await a.SetNoteAsync(ws, day, "from A");
        await b.SetNoteAsync(ws, day, "from B");
        for (var i = 0; i < 3; i++) { await _a.Client.SyncAsync(); await _b.Client.SyncAsync(); }

        (await a.GetNoteAsync(ws, day)).ShouldBe(await b.GetNoteAsync(ws, day));
        var id = (await _a.Db.RunAsync(s => s.DayNotes.GetAsync(ws, day, DayNoteKind.Note)))!.Id;
        var conflicts = (await _a.Db.RunAsync(s => s.Sync.ListConflictsAsync(id))).Concat(await _b.Db.RunAsync(s => s.Sync.ListConflictsAsync(id)));
        conflicts.ShouldContain(c => c.Field == "text");
    }

    [Fact]
    public async Task Tags_and_tag_assignments_sync_including_removal()
    {
        var ws = await SyncedWorkspaceAsync();
        var item = await _a.AddItemAsync(ws, "tagged");
        var tag = new Tag { Id = Guid.CreateVersion7(), WorkspaceId = ws, Name = "urgent", Color = "#f00" };
        await _a.Db.RunAsync(async s => { await s.Tags.UpsertAsync(tag); await s.Tags.SetItemTagsAsync(item, [tag.Id]); return 0; });

        var ops = await _a.PendingAsync();
        ops.ShouldContain(o => o.EntityType == EntityTypes.Tag && o.EntityId == tag.Id);
        ops.ShouldContain(o => o.EntityType == EntityTypes.TodoTag);

        await _a.Client.SyncAsync();
        await _b.Client.SyncAsync();
        (await _b.Db.RunAsync(s => s.Tags.GetItemTagIdsAsync(item))).ShouldBe([tag.Id]);
        (await _b.Db.RunAsync(s => s.Tags.ListAsync(ws))).Single().Name.ShouldBe("urgent");

        await _a.Db.RunAsync(async s => { await s.Tags.SetItemTagsAsync(item, []); return 0; });
        await _a.Client.SyncAsync();
        await _b.Client.SyncAsync();
        (await _b.Db.RunAsync(s => s.Tags.GetItemTagIdsAsync(item))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Links_sync_and_removal_is_a_tombstone()
    {
        var ws = await SyncedWorkspaceAsync();
        var item = await _a.AddItemAsync(ws, "see the PR");
        var indexer = new LinkIndexer(_a.Db, _clock);

        await indexer.AddExplicitAsync(item, "https://github.com/acme/app/pull/7");
        await _a.Client.SyncAsync();
        await _b.Client.SyncAsync();
        (await _b.Db.RunAsync(s => s.Links.ListForItemAsync(item))).Single().Url.ShouldBe("https://github.com/acme/app/pull/7");

        var linkId = (await _a.Db.RunAsync(s => s.Links.ListForItemAsync(item))).Single().Id;
        await _a.Db.RunAsync(async s => { await s.Links.RemoveAsync(linkId, DateTimeOffset.UtcNow); return 0; });
        await _a.Client.SyncAsync();
        await _b.Client.SyncAsync();
        (await _b.Db.RunAsync(s => s.Links.ListForItemAsync(item))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Merging_remote_changes_does_not_echo_them_back_as_local_ops()
    {
        var ws = await SyncedWorkspaceAsync();
        await new WorkspaceService(_a.Db, _clock).RenameAsync(ws, "from A");
        await _a.Client.SyncAsync();

        await _b.Client.SyncAsync();

        (await _b.PendingAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_workspace_with_sync_off_records_clocks_but_queues_nothing_for_any_writer()
    {
        var ws = await new WorkspaceService(_a.Db, _clock).CreateAsync("Local", "home", "#0f0", BuiltInPresets.Zen);
        await new WorkspaceService(_a.Db, _clock).RenameAsync(ws.Id, "Still local");
        await new RecurrenceService(_a.Db, _clock).CreateRuleAsync(ws.Id, "FREQ=DAILY", new RuleTemplate("x"), new DateOnly(2026, 10, 7));
        await new DayNoteService(_a.Db).SetNoteAsync(ws.Id, new DateOnly(2026, 10, 7), "note");

        (await _a.PendingAsync()).ShouldBeEmpty();
        (await _a.Db.RunAsync(s => s.Sync.GetClocksAsync(EntityTypes.Workspace, ws.Id))).ShouldContainKey("name");
    }

    [Fact]
    public async Task Enabling_sync_pushes_existing_rules_tags_notes_and_links_too()
    {
        var ws = await new WorkspaceService(_a.Db, _clock).CreateAsync("Work", "work", "#00f", BuiltInPresets.Sprint);
        var rule = await new RecurrenceService(_a.Db, _clock).CreateRuleAsync(ws.Id, "FREQ=DAILY", new RuleTemplate("r"), new DateOnly(2026, 10, 7));
        await new DayNoteService(_a.Db).SetNoteAsync(ws.Id, new DateOnly(2026, 10, 7), "keep");
        var item = await _a.AddItemAsync(ws.Id, "x");
        var tag = new Tag { Id = Guid.CreateVersion7(), WorkspaceId = ws.Id, Name = "t", Color = "#fff" };
        await _a.Db.RunAsync(async s => { await s.Tags.UpsertAsync(tag); await s.Tags.SetItemTagsAsync(item, [tag.Id]); return 0; });
        await new LinkIndexer(_a.Db, _clock).AddExplicitAsync(item, "https://example.com/doc");

        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();
        await _b.Workspaces.BootstrapNewDeviceAsync();

        (await _b.Db.RunAsync(s => s.Rules.GetAsync(rule.Id))).ShouldNotBeNull();
        (await _b.Db.RunAsync(s => s.DayNotes.GetAsync(ws.Id, new DateOnly(2026, 10, 7), DayNoteKind.Note)))!.Text.ShouldBe("keep");
        (await _b.Db.RunAsync(s => s.Tags.GetItemTagIdsAsync(item))).ShouldBe([tag.Id]);
        (await _b.Db.RunAsync(s => s.Links.ListForItemAsync(item))).Single().Url.ShouldBe("https://example.com/doc");
    }

    [Fact]
    public async Task All_recorded_ops_use_only_known_entity_types_and_fields()
    {
        var ws = await SyncedWorkspaceAsync();
        var item = await _a.AddItemAsync(ws, "x");
        await new RecurrenceService(_a.Db, _clock).CreateRuleAsync(ws, "FREQ=DAILY", new RuleTemplate("r"), new DateOnly(2026, 10, 7));
        await new DayNoteService(_a.Db).SetNoteAsync(ws, new DateOnly(2026, 10, 7), "n");
        await new LinkIndexer(_a.Db, _clock).AddExplicitAsync(item, "https://example.com");
        await new WorkspaceService(_a.Db, _clock).RenameAsync(ws, "y");

        foreach (var op in await _a.PendingAsync())
        {
            EntityTypes.All.ShouldContain(op.EntityType);
            var allowed = SyncRows.FieldsOf(op.EntityType)!;
            if (op.Kind == OpKinds.Set) allowed.ShouldContain(op.Field!);
            else ((JsonObject)op.Value!).Select(kv => kv.Key).Where(k => k != "id").ShouldAllBe(k => allowed.Contains(k));
        }
    }
}
