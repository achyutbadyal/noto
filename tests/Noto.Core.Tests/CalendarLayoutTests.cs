using Noto.Core.Commands;
using Noto.Core.Layouts;
using Noto.Core.Models;
using static Noto.Core.Tests.Story;

namespace Noto.Core.Tests;

public class CalendarLayoutTests
{
    static readonly DateOnly Today = Oct(14); // a Wednesday

    static TodoItem Item(Action<TodoItem>? tweak = null)
    {
        var i = Make.Item();
        tweak?.Invoke(i);
        return i;
    }

    [Fact]
    public void Weeks_start_on_monday()
    {
        CalendarLayout.WeekStart(Today).ShouldBe(Oct(12));
        CalendarLayout.WeekStart(Oct(18)).ShouldBe(Oct(12)); // Sunday belongs to the week before it
        CalendarLayout.WeekStart(Oct(19)).ShouldBe(Oct(19));
    }

    [Fact]
    public void Month_grid_starts_on_the_monday_on_or_before_the_first_and_spans_whole_weeks()
    {
        var start = CalendarLayout.GridStart(Today, CalendarMode.Month);
        start.ShouldBe(new DateOnly(Today.Year, 9, 28));
        start.DayOfWeek.ShouldBe(DayOfWeek.Monday);

        var weeks = CalendarLayout.GridWeeks(Today, CalendarMode.Month);
        CalendarLayout.Build([], Today, Today, CalendarMode.Month).Days.Count.ShouldBe(weeks * 7);
        CalendarLayout.GridWeeks(Today, CalendarMode.Week).ShouldBe(1);
    }

    [Fact]
    public void Items_sit_on_their_planned_day_else_their_due_day()
    {
        var planned = Item(i => i.PlannedFor = Oct(15));
        var dueOnly = Item(i => i.DueDate = Oct(16));
        var both = Item(i =>
        {
            i.PlannedFor = Oct(15);
            i.DueDate = Oct(20);
        });

        var view = CalendarLayout.Build([planned, dueOnly, both], Today, Today, CalendarMode.Week);

        view.Days.Single(d => d.Day == Oct(15)).Open.ShouldBe([planned, both], ignoreOrder: true);
        view.Days.Single(d => d.Day == Oct(16)).Open.ShouldBe([dueOnly]);
    }

    [Fact]
    public void Past_open_items_go_to_overdue_and_dateless_ones_to_the_tray()
    {
        var late = Item(i => i.PlannedFor = Oct(10));
        var free = Item();
        var someday = Item(i => i.IsSomeday = true);
        var dropped = Item(i => i.Status = ItemStatus.Dropped);

        var view = CalendarLayout.Build(
            [late, free, someday, dropped],
            Today,
            Today,
            CalendarMode.Week
        );

        view.Overdue.ShouldBe([late]);
        view.Unscheduled.ShouldBe([free]);
    }

    [Fact]
    public void Done_items_stay_on_the_day_they_were_completed_and_containers_are_never_shown()
    {
        var done = Item(i =>
        {
            i.Status = ItemStatus.Done;
            i.CompletedOn = Oct(13);
        });
        var container = Item(i => i.IsContainer = true);

        var view = CalendarLayout.Build([done, container], Today, Today, CalendarMode.Week);

        view.Days.Single(d => d.Day == Oct(13)).Done.ShouldBe([done]);
        view.Unscheduled.ShouldBeEmpty();
    }

    [Fact]
    public void Planning_for_a_day_is_one_plan_command()
    {
        var item = Item(i => i.PlannedFor = Today);

        var commands = CalendarMoves.Plan(item, Oct(17), Today);

        var plan = commands.ShouldHaveSingleItem().ShouldBeOfType<PlanItem>();
        plan.To.ShouldBe(Oct(17));
        plan.Kind.ShouldBe(PlanKind.Plan);
    }

    [Fact]
    public void Nothing_happens_for_the_same_day_a_past_day_or_a_finished_item()
    {
        var item = Item(i => i.PlannedFor = Today);
        CalendarMoves.Plan(item, Today, Today).ShouldBeEmpty();
        CalendarMoves.Plan(item, Oct(10), Today).ShouldBeEmpty();
        CalendarMoves.CanDropOn(Oct(10), Today).ShouldBeFalse();

        var done = Item(i => i.Status = ItemStatus.Done);
        CalendarMoves.Plan(done, Oct(17), Today).ShouldBeEmpty();
    }

    [Fact]
    public void Dropping_on_the_tray_unschedules_only_a_planned_item()
    {
        var planned = Item(i => i.PlannedFor = Oct(15));
        CalendarMoves
            .Plan(planned, null, Today)
            .ShouldHaveSingleItem()
            .ShouldBeOfType<PlanItem>()
            .Kind.ShouldBe(PlanKind.Unschedule);

        CalendarMoves.Plan(Item(), null, Today).ShouldBeEmpty();
    }
}
