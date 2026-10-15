using Noto.Core.Layouts;
using Noto.Core.Links;
using Noto.Core.Models;
using static Noto.Core.Tests.Story;

namespace Noto.Core.Tests;

public class DayAheadLayoutTests
{
    static readonly DateOnly Today = Oct(14);
    static readonly DateOnly Day = Oct(17);

    static TodoItem Item(Action<TodoItem>? tweak = null)
    {
        var i = Make.Item();
        tweak?.Invoke(i);
        return i;
    }

    static DayAheadPlan Plan(params TodoItem[] items) =>
        DayAheadLayout.Build(items, Today, Day, Make.Workspace(), 30);

    [Fact]
    public void Planned_items_group_by_part_of_the_day_with_anytime_last()
    {
        var plan = Plan(
            Item(i => i.PlannedFor = Day),
            Item(i =>
            {
                i.PlannedFor = Day;
                i.TimeOfDay = TimeOfDay.Afternoon;
            }),
            Item(i =>
            {
                i.PlannedFor = Day;
                i.TimeOfDay = TimeOfDay.Morning;
            })
        );

        plan.Parts.Select(p => p.Part).ShouldBe([TimeOfDay.Morning, TimeOfDay.Afternoon, null]);
        plan.PlannedCount.ShouldBe(3);
    }

    [Fact]
    public void Load_counts_estimates_and_falls_back_for_unestimated_work()
    {
        var plan = Plan(
            Item(i =>
            {
                i.PlannedFor = Day;
                i.EstimateMinutes = 90;
            }),
            Item(i => i.PlannedFor = Day)
        );

        plan.Committed.ShouldBe(120);
        plan.Unestimated.ShouldBe(1);
        plan.Capacity.ShouldBe(360);
        plan.Free.ShouldBe(240);
        plan.IsOver.ShouldBeFalse();
    }

    [Fact]
    public void Done_dropped_someday_and_container_items_do_not_load_the_day()
    {
        var plan = Plan(
            Item(i =>
            {
                i.PlannedFor = Day;
                i.Status = ItemStatus.Done;
            }),
            Item(i =>
            {
                i.PlannedFor = Day;
                i.Status = ItemStatus.Dropped;
            }),
            Item(i =>
            {
                i.PlannedFor = Day;
                i.IsSomeday = true;
            }),
            Item(i =>
            {
                i.PlannedFor = Day;
                i.IsContainer = true;
            })
        );

        plan.PlannedCount.ShouldBe(0);
        plan.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_deadline_on_the_day_shows_even_when_the_work_is_planned_elsewhere()
    {
        var planned = Item(i =>
        {
            i.PlannedFor = Day;
            i.DueDate = Day;
        });
        var elsewhere = Item(i =>
        {
            i.PlannedFor = Oct(15);
            i.DueDate = Day;
        });
        var undated = Item(i => i.DueDate = Day);

        var plan = Plan(planned, elsewhere, undated);

        plan.DueOnly.ShouldBe([elsewhere, undated], ignoreOrder: true);
        plan.DueOnly.ShouldNotContain(planned);
    }

    [Fact]
    public void Waiting_items_are_listed_apart_and_do_not_count_as_work()
    {
        var waiting = Item(i =>
        {
            i.PlannedFor = Day;
            i.Status = ItemStatus.Waiting;
            i.WaitingOn = "Priya";
        });

        var plan = Plan(waiting);

        plan.Waiting.ShouldBe([waiting]);
        plan.Committed.ShouldBe(0);
    }

    [Fact]
    public void Suggestions_put_nearby_deadlines_before_undecided_and_unscheduled_work()
    {
        var soon = Item(i => i.DueDate = Oct(19));
        var farAway = Item(i => i.DueDate = Oct(30));
        var stale = Item(i => i.PlannedFor = Oct(10));
        var loose = Item(i => i.Priority = 4);
        var alreadyHere = Item(i => i.PlannedFor = Day);
        var future = Item(i => i.PlannedFor = Oct(20));

        var plan = Plan(soon, farAway, stale, loose, alreadyHere, future);

        plan.Suggestions.Select(s => s.Item).ShouldBe([soon, stale, loose]);
        plan.Suggestions[0].Reason.ShouldBe(SuggestionReason.DueSoon);
        plan.Suggestions[1].Reason.ShouldBe(SuggestionReason.Overdue);
        plan.Suggestions[2].Reason.ShouldBe(SuggestionReason.HighPriority);
    }

    [Fact]
    public void An_overdue_deadline_is_called_overdue()
    {
        var late = Item(i => i.DueDate = Oct(12));

        Plan(late).Suggestions.Single().Reason.ShouldBe(SuggestionReason.Overdue);
    }

    [Fact]
    public void Suggestions_are_capped()
    {
        var items = Enumerable.Range(0, 12).Select(_ => Item()).ToArray();

        Plan(items).Suggestions.Count.ShouldBe(5);
    }

    [Fact]
    public void Link_spans_report_where_each_url_sits_and_drop_trailing_punctuation()
    {
        var text = "see https://a.test/x, and (https://b.test/y).";

        var spans = LinkUrl.Spans(text);

        spans.Select(s => s.Url).ShouldBe(["https://a.test/x", "https://b.test/y"]);
        text.Substring(spans[0].Start, spans[0].Length).ShouldBe("https://a.test/x");
        text.Substring(spans[1].Start, spans[1].Length).ShouldBe("https://b.test/y");
        LinkUrl.Spans(null).ShouldBeEmpty();
        LinkUrl.Spans("no links here").ShouldBeEmpty();
    }
}
