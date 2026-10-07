using Noto.Core.Derivations;
using Noto.Core.Models;

namespace Noto.Core.Tests;

public class TodayQueryTests
{
    static readonly DateOnly Today = new(2026, 10, 7);
    readonly Workspace _ws = Make.Workspace();

    TodoItem Item(DateOnly? planned, ItemStatus status = ItemStatus.Open)
    {
        var i = Make.Item(_ws.Id);
        i.PlannedFor = planned;
        i.Status = status;
        if (status == ItemStatus.Waiting) i.WaitingOn = "bob";
        return i;
    }

    TodayView Build(params TodoItem[] items) =>
        TodayQuery.Build(items, _ws, Today, i => new ItemMetrics(0, i.PlannedFor is { } p ? Today.DayNumber - p.DayNumber : 0, 0));

    [Fact]
    public void Past_planned_items_appear_and_need_a_decision()
    {
        var view = Build(Item(Today), Item(Today.AddDays(-2)), Item(Today.AddDays(1)));
        view.Planned.Count.ShouldBe(2);
        view.NeedsDecision(Today).ShouldBe(1);
    }

    [Fact]
    public void Someday_containers_and_unplanned_items_are_excluded()
    {
        var someday = Item(null); someday.IsSomeday = true;
        var container = Item(null); container.IsContainer = true;
        Build(someday, container, Item(null)).Planned.ShouldBeEmpty();
    }

    [Fact]
    public void Waiting_is_separate_and_future_planned_waiting_stays_hidden()
    {
        var view = Build(Item(Today, ItemStatus.Waiting), Item(null, ItemStatus.Waiting), Item(Today.AddDays(3), ItemStatus.Waiting));
        view.Waiting.Count.ShouldBe(2);
        view.Planned.ShouldBeEmpty();
    }

    [Fact]
    public void Done_today_uses_the_credited_day()
    {
        var yesterday = Item(Today.AddDays(-1), ItemStatus.Done);
        yesterday.CompletedOn = Today.AddDays(-1); yesterday.CompletedAt = DateTimeOffset.UtcNow;
        var todays = Item(Today, ItemStatus.Done);
        todays.CompletedOn = Today; todays.CompletedAt = DateTimeOffset.UtcNow;

        Build(yesterday, todays).DoneToday.ShouldHaveSingleItem().Id.ShouldBe(todays.Id);
    }

    [Fact]
    public void Now_item_is_lifted_out_of_planned()
    {
        var now = Item(Today);
        _ws.NowItemId = now.Id;
        var view = Build(now, Item(Today));
        view.Now!.Id.ShouldBe(now.Id);
        view.Planned.Count.ShouldBe(1);
    }

    [Fact]
    public void Relentless_pins_the_three_highest_carry_items()
    {
        _ws.Pressure = Pressure.Relentless;
        var items = Enumerable.Range(0, 5).Select(n => Item(Today.AddDays(-n))).ToArray();

        var view = Build(items);

        view.Pinned.Count.ShouldBe(3);
        view.Pinned.Select(i => i.PlannedFor).ShouldBe([Today.AddDays(-4), Today.AddDays(-3), Today.AddDays(-2)]);
        view.Planned.Count.ShouldBe(2);
    }
}
