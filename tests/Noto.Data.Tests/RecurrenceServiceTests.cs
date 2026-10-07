using Noto.Core.Commands;
using Noto.Core.Derivations;
using Noto.Core.Habits;
using Noto.Core.Models;
using Noto.Core.Presets;
using Noto.Core.Recurrence;
using Noto.Core.Tests;
using Noto.Core.Workspaces;

namespace Noto.Data.Tests;

public class RecurrenceServiceTests : IDisposable
{
    readonly FeatureFixture _f = new();
    readonly RecurrenceService _svc;

    public RecurrenceServiceTests() => _svc = new RecurrenceService(_f.Uow, _f.Clock);

    public void Dispose() => _f.Dispose();

    static readonly DateOnly Oct5 = new(2026, 10, 5);

    Task<RecurrenceRule> Rule(string rrule = "FREQ=DAILY", MissedBehavior missed = MissedBehavior.Carry, DateOnly? start = null, Guid[]? tags = null) =>
        _svc.CreateRuleAsync(_f.Ws.Id, rrule, new RuleTemplate("Standup notes", EstimateMinutes: 10, TagIds: tags), start ?? Oct5, missed);

    [Fact]
    public async Task Rules_round_trip_through_the_repository()
    {
        var rule = await _svc.CreateRuleAsync(_f.Ws.Id, "FREQ=WEEKLY;BYDAY=MO", new RuleTemplate("Run", Priority: 2, TimeOfDay: TimeOfDay.Morning),
            Oct5, MissedBehavior.Skip, targetCount: 3, targetPeriod: TargetPeriod.Week, end: new DateOnly(2027, 1, 1));

        var back = (await _f.Uow.RunAsync(s => s.Rules.GetAsync(rule.Id)))!;

        (back.RRule, back.MissedBehavior, back.TargetCount, back.TargetPeriod, back.EndDate)
            .ShouldBe(("FREQ=WEEKLY;BYDAY=MO", MissedBehavior.Skip, 3, TargetPeriod.Week, new DateOnly(2027, 1, 1)));
        back.Template.ShouldBe(new RuleTemplate("Run", null, null, 2, TimeOfDay.Morning, []), new RuleTemplateComparer());
    }

    sealed class RuleTemplateComparer : IEqualityComparer<RuleTemplate>
    {
        public bool Equals(RuleTemplate? a, RuleTemplate? b) =>
            a is not null && b is not null && a.Title == b.Title && a.Priority == b.Priority && a.TimeOfDay == b.TimeOfDay;
        public int GetHashCode(RuleTemplate t) => t.Title.GetHashCode();
    }

    [Fact]
    public async Task Invalid_rules_are_rejected()
    {
        await Should.ThrowAsync<FormatException>(() => Rule("FREQ=YEARLY"));
        await Should.ThrowAsync<CommandException>(() => _svc.CreateRuleAsync(_f.Ws.Id, "FREQ=DAILY", new RuleTemplate(" "), Oct5));
        await Should.ThrowAsync<CommandException>(() => _svc.CreateRuleAsync(_f.Ws.Id, "FREQ=DAILY", new RuleTemplate("x"), Oct5, targetCount: 3));
    }

    [Fact]
    public async Task Generates_today_plus_the_most_recent_missed_carry_instance_with_history()
    {
        var rule = await Rule(start: Oct5); // today is Wed Oct 7; Oct 5 and 6 were never generated

        var created = await _svc.GenerateDueAsync(_f.Ws.Id);

        created.Select(i => i.OccurrenceDate).ShouldBe([new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 7)]);
        created.ShouldAllBe(i => i.RecurrenceRuleId == rule.Id && i.Title == "Standup notes");
        var yesterday = created[0];
        (await _f.Uow.RunAsync(s => s.Events.ListForItemAsync(yesterday.Id))).ShouldHaveSingleItem().Type.ShouldBe(ItemEventType.Created);

        // The carried instance has 1 day of carry as of today and the missed older one stays un-materialized.
        var metrics = await _f.Derive.GetMetricsAsync(_f.Ws.Id, [yesterday]);
        (metrics[yesterday.Id].Age, metrics[yesterday.Id].Carry).ShouldBe((1, 1));
        (await _f.Uow.RunAsync(s => s.Items.GetAsync(RecurrenceEngine.InstanceId(rule, Oct5)))).ShouldBeNull();
    }

    [Fact]
    public async Task Generation_is_idempotent()
    {
        await Rule();
        (await _svc.GenerateDueAsync(_f.Ws.Id)).Count.ShouldBe(2);
        (await _svc.GenerateDueAsync(_f.Ws.Id)).ShouldBeEmpty();
        (await _f.Uow.RunAsync(s => s.Items.ListAsync(_f.Ws.Id))).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Skip_rules_only_create_todays_instance()
    {
        await Rule(missed: MissedBehavior.Skip);
        var created = await _svc.GenerateDueAsync(_f.Ws.Id);
        created.ShouldHaveSingleItem().OccurrenceDate.ShouldBe(new DateOnly(2026, 10, 7));
    }

    [Fact]
    public async Task A_deleted_instance_is_not_regenerated()
    {
        var rule = await Rule(missed: MissedBehavior.Skip);
        var item = (await _svc.GenerateDueAsync(_f.Ws.Id)).Single();
        await _f.Bus.SendAsync(new DeleteItem(item.Id));

        (await _svc.GenerateDueAsync(_f.Ws.Id)).ShouldBeEmpty();
        rule.Id.ShouldBe(item.RecurrenceRuleId!.Value);
    }

    [Fact]
    public async Task Two_devices_generating_the_same_day_produce_identical_rows()
    {
        var rule = await Rule(missed: MissedBehavior.Skip);
        using var other = SqliteUnitOfWork.InMemory();
        await other.RunAsync(async s =>
        {
            await s.Workspaces.UpsertAsync(_f.Ws);
            await s.Rules.UpsertAsync(rule);
            return 0;
        });

        var a = (await _svc.GenerateDueAsync(_f.Ws.Id)).Single();
        var b = (await new RecurrenceService(other, _f.Clock).GenerateDueAsync(_f.Ws.Id)).Single();

        b.Id.ShouldBe(a.Id);
        (b.CreatedAt, b.CreatedTz, b.Title, b.PlannedFor).ShouldBe((a.CreatedAt, a.CreatedTz, a.Title, a.PlannedFor));
        var ea = (await _f.Uow.RunAsync(s => s.Events.ListForItemAsync(a.Id))).Single();
        var eb = (await other.RunAsync(s => s.Events.ListForItemAsync(b.Id))).Single();
        eb.Id.ShouldBe(ea.Id);
    }

    [Fact]
    public async Task Template_edits_only_affect_instances_not_yet_generated()
    {
        var rule = await Rule(missed: MissedBehavior.Skip);
        var first = (await _svc.GenerateDueAsync(_f.Ws.Id)).Single();

        await _svc.UpdateAsync(rule.Id, r => r.Template = r.Template with { Title = "Renamed" });
        _f.Clock.Advance(TimeSpan.FromDays(1));
        var second = (await _svc.GenerateDueAsync(_f.Ws.Id)).Single();

        (await _f.LoadAsync(first.Id)).Title.ShouldBe("Standup notes");
        second.Title.ShouldBe("Renamed");
    }

    [Fact]
    public async Task Deleted_rules_stop_generating_but_keep_existing_instances()
    {
        var rule = await Rule(missed: MissedBehavior.Skip);
        await _svc.GenerateDueAsync(_f.Ws.Id);
        await _svc.DeleteAsync(rule.Id);

        _f.Clock.Advance(TimeSpan.FromDays(1));
        (await _svc.GenerateDueAsync(_f.Ws.Id)).ShouldBeEmpty();
        (await _f.Uow.RunAsync(s => s.Items.ListAsync(_f.Ws.Id))).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Template_tags_are_applied_to_generated_instances()
    {
        var tag = new Tag { Id = Guid.CreateVersion7(), WorkspaceId = _f.Ws.Id, Name = "daily" };
        await _f.Uow.RunAsync(async s => { await s.Tags.UpsertAsync(tag); return 0; });
        await Rule(missed: MissedBehavior.Skip, tags: [tag.Id]);

        var item = (await _svc.GenerateDueAsync(_f.Ws.Id)).Single();

        (await _f.Uow.RunAsync(s => s.Tags.GetItemTagIdsAsync(item.Id))).ShouldBe([tag.Id]);
    }

    [Fact]
    public async Task Habit_stats_over_generated_and_completed_instances()
    {
        var rule = await Rule(missed: MissedBehavior.Skip, start: new DateOnly(2026, 10, 5));
        foreach (var _ in Enumerable.Range(0, 3))
        {
            var today = (await _svc.GenerateDueAsync(_f.Ws.Id)).Single(); // one instance per day
            await _f.Bus.SendAsync(new CompleteItem(today.Id));
            _f.Clock.Advance(TimeSpan.FromDays(1));
        } // Oct 7, 8, 9 done

        var instances = await _f.Uow.RunAsync(s => s.Items.ListAsync(_f.Ws.Id));
        var stats = HabitCalculator.Compute(rule, instances, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 5));

        stats.CurrentStreak.ShouldBe(3);
        stats.Heatmap.Single(c => c.Day == new DateOnly(2026, 10, 5)).State.ShouldBe(HabitDayState.Missed);
    }
}

public class AuxRepositoryTests : IDisposable
{
    readonly FeatureFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Tags_and_item_tags_round_trip_and_replace()
    {
        var id = await _f.CreateItemAsync("x");
        var a = new Tag { Id = Guid.CreateVersion7(), WorkspaceId = _f.Ws.Id, Name = "alpha", Color = "#111" };
        var b = new Tag { Id = Guid.CreateVersion7(), WorkspaceId = _f.Ws.Id, Name = "beta", Color = "#222" };

        await _f.Uow.RunAsync(async s =>
        {
            await s.Tags.UpsertAsync(a);
            await s.Tags.UpsertAsync(b);
            await s.Tags.SetItemTagsAsync(id, [a.Id, b.Id, a.Id]);
            return 0;
        });
        (await _f.Uow.RunAsync(s => s.Tags.GetItemTagIdsAsync(id))).Count.ShouldBe(2);

        await _f.Uow.RunAsync(async s => { await s.Tags.SetItemTagsAsync(id, [b.Id]); return 0; });
        (await _f.Uow.RunAsync(s => s.Tags.ListItemTagsAsync(_f.Ws.Id))).ShouldBe([(id, b.Id)]);
        (await _f.Uow.RunAsync(s => s.Tags.ListAsync(_f.Ws.Id))).Select(t => t.Name).ShouldBe(["alpha", "beta"]);
    }

    [Fact]
    public async Task Day_notes_upsert_by_kind_and_day()
    {
        var notes = new DayNoteService(_f.Uow);
        var day = new DateOnly(2026, 10, 7);

        await notes.SetNoteAsync(_f.Ws.Id, day, "  call Sam ");
        await notes.SetNoteAsync(_f.Ws.Id, day, "call Sam about Q4");
        (await notes.GetNoteAsync(_f.Ws.Id, day)).ShouldBe("call Sam about Q4");
        (await notes.GetNoteAsync(_f.Ws.Id, day.AddDays(1))).ShouldBeNull();
        (await _f.Uow.RunAsync(s => s.DayNotes.ListAsync(_f.Ws.Id))).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Weekly_outcomes_are_capped_at_three_and_keyed_to_the_monday()
    {
        var notes = new DayNoteService(_f.Uow);
        var wednesday = new DateOnly(2026, 10, 14);

        await notes.SetOutcomesAsync(_f.Ws.Id, wednesday, ["a", " ", "b", "c", "d"]);

        (await notes.GetOutcomesAsync(_f.Ws.Id, new DateOnly(2026, 10, 12))).ShouldBe(["a", "b", "c"]);
        (await notes.GetNoteAsync(_f.Ws.Id, new DateOnly(2026, 10, 12))).ShouldBeNull(); // separate from the shutdown note
        (await notes.GetOutcomesAsync(_f.Ws.Id, new DateOnly(2026, 10, 19))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Notes_for_unknown_workspaces_are_rejected()
    {
        await Should.ThrowAsync<CommandException>(() => new DayNoteService(_f.Uow).SetNoteAsync(Guid.NewGuid(), new DateOnly(2026, 10, 7), "x"));
    }
}
