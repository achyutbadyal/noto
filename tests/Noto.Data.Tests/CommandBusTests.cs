using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Tests;
using Noto.Data;

namespace Noto.Data.Tests;

public sealed class CommandBusTests : IDisposable
{
    readonly SqliteUnitOfWork _uow = SqliteUnitOfWork.InMemory();
    readonly FakeClock _clock = new(DateTimeOffset.Parse("2026-10-07T10:00:00Z"));
    readonly Workspace _ws = Make.Workspace();
    readonly CommandBus _bus;

    public CommandBusTests()
    {
        _bus = new CommandBus(_uow, _clock, Guid.CreateVersion7());
        _uow.RunAsync(async s =>
            {
                await s.Workspaces.UpsertAsync(_ws);
                return 0;
            })
            .GetAwaiter()
            .GetResult();
    }

    public void Dispose() => _uow.Dispose();

    async Task<Guid> CreateAsync(string title = "Deploy", DateOnly? plannedFor = null)
    {
        var id = Guid.CreateVersion7();
        await _bus.SendAsync(
            new CreateItem(id, _ws.Id, title, plannedFor ?? new DateOnly(2026, 10, 7))
        );
        return id;
    }

    Task<TodoItem> Load(Guid id) => _uow.RunAsync(async s => (await s.Items.GetAsync(id))!);

    Task<IReadOnlyList<ItemEvent>> Events(Guid id) =>
        _uow.RunAsync(s => s.Events.ListForItemAsync(id));

    [Fact]
    public async Task Create_persists_item_and_a_created_event()
    {
        var id = await CreateAsync();

        var item = await Load(id);
        item.Title.ShouldBe("Deploy");
        item.PlannedFor.ShouldBe(new DateOnly(2026, 10, 7));
        item.CreatedTz.ShouldBe("UTC");
        (await Events(id)).ShouldHaveSingleItem().Type.ShouldBe(ItemEventType.Created);
    }

    [Fact]
    public async Task Complete_then_undo_restores_state_and_appends_a_compensating_event()
    {
        var id = await CreateAsync();

        var result = await _bus.SendAsync(new CompleteItem(id));
        var done = await Load(id);
        done.Status.ShouldBe(ItemStatus.Done);
        done.CompletedOn.ShouldBe(new DateOnly(2026, 10, 7));

        await _bus.UndoAsync(result.UndoToken);
        var reopened = await Load(id);
        reopened.Status.ShouldBe(ItemStatus.Open);
        reopened.CompletedOn.ShouldBeNull();
        (await Events(id))
            .Select(e => e.Type)
            .ShouldBe([ItemEventType.Created, ItemEventType.Completed, ItemEventType.Reopened]);
    }

    [Fact]
    public async Task Undo_of_create_tombstones_the_item()
    {
        var id = Guid.CreateVersion7();
        var result = await _bus.SendAsync(new CreateItem(id, _ws.Id, "Oops"));

        await _bus.UndoAsync(result.UndoToken);

        (await Load(id)).DeletedAt.ShouldNotBeNull();
        (await _uow.RunAsync(s => s.Items.ListAsync(_ws.Id))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Undo_token_is_single_use()
    {
        var result = await _bus.SendAsync(new CreateItem(Guid.CreateVersion7(), _ws.Id, "x"));
        await _bus.UndoAsync(result.UndoToken);
        await Should.ThrowAsync<CommandException>(() => _bus.UndoAsync(result.UndoToken));
    }

    [Fact]
    public async Task Keep_today_sets_planned_for_today_and_records_the_move()
    {
        var id = await CreateAsync(plannedFor: new DateOnly(2026, 10, 3));

        await _bus.SendAsync(new PlanItem(id, null, PlanKind.KeepToday));

        (await Load(id)).PlannedFor.ShouldBe(new DateOnly(2026, 10, 7));
        var planned = (await Events(id)).Last();
        planned.Type.ShouldBe(ItemEventType.Planned);
        planned.Data!["kind"]!.GetValue<string>().ShouldBe("keep_today");
        planned.Data["from"]!.GetValue<string>().ShouldBe("2026-10-03");
    }

    [Fact]
    public async Task Defer_requires_a_future_date()
    {
        var id = await CreateAsync();
        await Should.ThrowAsync<CommandException>(() =>
            _bus.SendAsync(new PlanItem(id, new DateOnly(2026, 10, 7), PlanKind.Defer))
        );
    }

    [Fact]
    public async Task Completing_yesterday_credits_the_earlier_logical_day()
    {
        var id = await CreateAsync();
        await _bus.SendAsync(new CompleteItem(id, new DateOnly(2026, 10, 6)));
        (await Load(id)).CompletedOn.ShouldBe(new DateOnly(2026, 10, 6));
    }

    [Fact]
    public async Task Waiting_round_trip_keeps_planned_for()
    {
        var id = await CreateAsync();
        await _bus.SendAsync(new StartWaiting(id, "alice"));
        var waiting = await Load(id);
        waiting.Status.ShouldBe(ItemStatus.Waiting);
        waiting.PlannedFor.ShouldNotBeNull();

        await _bus.SendAsync(new EndWaiting(id));
        (await Load(id)).WaitingOn.ShouldBeNull();
    }

    [Fact]
    public async Task Drop_and_restore()
    {
        var id = await CreateAsync();
        await _bus.SendAsync(new DropItem(id, DropReason.NotNeeded, "dup"));
        (await Load(id)).Status.ShouldBe(ItemStatus.Dropped);

        await _bus.SendAsync(new RestoreItem(id));
        var item = await Load(id);
        item.Status.ShouldBe(ItemStatus.Open);
        item.DroppedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Planning_a_someday_item_clears_someday()
    {
        var id = Guid.CreateVersion7();
        await _bus.SendAsync(new CreateItem(id, _ws.Id, "Maybe", IsSomeday: true));
        await _bus.SendAsync(new PlanItem(id, new DateOnly(2026, 10, 8), PlanKind.Plan));
        (await Load(id)).IsSomeday.ShouldBeFalse();
    }

    [Fact]
    public async Task Rejected_command_writes_nothing()
    {
        var id = await CreateAsync();
        await _bus.SendAsync(new CompleteItem(id));

        await Should.ThrowAsync<CommandException>(() =>
            _bus.SendAsync(new StartWaiting(id, "bob"))
        );

        (await Events(id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Event_uses_the_workspace_zone_and_instant()
    {
        var id = await CreateAsync();
        var e = (await Events(id)).Single();
        e.Tz.ShouldBe("UTC");
        e.OccurredAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task Break_down_creates_children_and_first_inherits_the_plan()
    {
        var id = await CreateAsync("Ship v2", new DateOnly(2026, 10, 7));

        await _bus.SendAsync(new BreakDown(id, ["design", "build", "test"]));

        var parent = await Load(id);
        parent.IsContainer.ShouldBeTrue();
        parent.PlannedFor.ShouldBeNull();
        var kids = (await _uow.RunAsync(s => s.Items.ListAsync(_ws.Id)))
            .Where(i => i.ParentId == id)
            .OrderBy(i => i.Title)
            .ToList();
        kids.Count.ShouldBe(3);
        kids.Single(k => k.Title == "design").PlannedFor.ShouldBe(new DateOnly(2026, 10, 7));
        kids.Where(k => k.Title != "design").ShouldAllBe(k => k.PlannedFor == null);
    }

    [Fact]
    public async Task Break_down_undo_removes_children_and_restores_the_parent()
    {
        var id = await CreateAsync();
        var result = await _bus.SendAsync(new BreakDown(id, ["a", "b"]));

        await _bus.UndoAsync(result.UndoToken);

        var parent = await Load(id);
        parent.IsContainer.ShouldBeFalse();
        parent.PlannedFor.ShouldBe(new DateOnly(2026, 10, 7));
        (await _uow.RunAsync(s => s.Items.ListAsync(_ws.Id))).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Break_down_needs_two_to_five_titles()
    {
        var id = await CreateAsync();
        await Should.ThrowAsync<CommandException>(() =>
            _bus.SendAsync(new BreakDown(id, ["only one"]))
        );
        await Should.ThrowAsync<CommandException>(() =>
            _bus.SendAsync(new BreakDown(id, ["1", "2", "3", "4", "5", "6"]))
        );
    }

    [Fact]
    public async Task Field_setters_record_events_and_undo_restores_values()
    {
        var id = await CreateAsync();
        var r = await _bus.SendAsync(new SetEstimate(id, 45));
        await _bus.SendAsync(new SetPriority(id, 3));
        await _bus.SendAsync(new SetDueDate(id, new DateOnly(2026, 10, 20)));

        var item = await Load(id);
        (item.EstimateMinutes, item.Priority, item.DueDate).ShouldBe(
            (45, 3, new DateOnly(2026, 10, 20))
        );

        await _bus.UndoAsync(r.UndoToken);
        (await Load(id)).EstimateMinutes.ShouldBeNull();
    }

    [Fact]
    public async Task Stuck_reason_is_event_only()
    {
        var id = await CreateAsync();
        var before = await Load(id);
        await _bus.SendAsync(new GiveStuckReason(id, StuckReason.TooBig));

        (await Events(id)).Last().Data!["reason"]!.GetValue<string>().ShouldBe("too_big");
        (await Load(id)).Title.ShouldBe(before.Title);
    }

    [Fact]
    public async Task Delete_is_a_tombstone_and_undo_brings_it_back()
    {
        var id = await CreateAsync();
        var r = await _bus.SendAsync(new DeleteItem(id));
        (await Load(id)).DeletedAt.ShouldNotBeNull();

        await _bus.UndoAsync(r.UndoToken);
        (await Load(id)).DeletedAt.ShouldBeNull();
    }
}
