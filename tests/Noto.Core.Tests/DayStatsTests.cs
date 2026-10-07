using Noto.Core.Derivations;
using static Noto.Core.Tests.Story;

namespace Noto.Core.Tests;

public class DayStatsTests
{
    static DayStats Stats(DateOnly day, params Story[] stories) =>
        DayStatsCalculator.Compute(
            day,
            stories.Select(s => s.History()).ToList(),
            fallbackEstimateMinutes: 30
        );

    [Fact]
    public void Splits_the_day_into_carried_planned_and_added()
    {
        var carried = new Story(Oct(5), Oct(5));
        var planned = new Story(Oct(5), Oct(7));
        var added = new Story(Oct(7), Oct(7));

        var s = Stats(Oct(7), carried, planned, added);

        (s.CarriedIn, s.PlannedIn, s.Added).ShouldBe((1, 1, 1));
        s.OpenAtEnd.ShouldBe(3);
        s.CompletionRate.ShouldBe(0);
    }

    [Fact]
    public void Completion_rate_is_done_over_done_plus_left_over()
    {
        var done = new Story(Oct(7), Oct(7)).Complete(Oct(7));
        var left = new Story(Oct(7), Oct(7));

        var s = Stats(Oct(7), done, left);

        s.Done.ShouldBe(1);
        s.OpenAtEnd.ShouldBe(1);
        s.CompletionRate.ShouldBe(0.5);
    }

    [Fact]
    public void Rate_is_undefined_not_zero_for_an_empty_day()
    {
        Stats(Oct(7)).CompletionRate.ShouldBeNull();
    }

    [Fact]
    public void Already_done_credited_to_the_day_is_not_left_over()
    {
        // Completed on the 8th but credited to the 7th.
        var story = new Story(Oct(7), Oct(7)).Complete(Oct(8), credited: Oct(7));

        var s = Stats(Oct(7), story);

        s.Done.ShouldBe(1);
        s.OpenAtEnd.ShouldBe(0);
    }

    [Fact]
    public void Deferred_and_dropped_items_are_counted_on_their_day()
    {
        var deferred = new Story(Oct(7), Oct(7)).Plan(Oct(7), Oct(9), "defer");
        var dropped = new Story(Oct(7), Oct(7)).Drop(Oct(7));

        var s = Stats(Oct(7), deferred, dropped);

        s.DeferredOut.ShouldBe(1);
        s.Dropped.ShouldBe(1);
        s.OpenAtEnd.ShouldBe(0); // deferred item is planned past D; dropped is terminal
    }

    [Fact]
    public void Someday_and_waiting_items_are_not_committed()
    {
        var someday = new Story(Oct(5), Oct(5)).Someday(Oct(6), true);
        var waiting = new Story(Oct(5), Oct(5)).Wait(Oct(6));

        var s = Stats(Oct(8), someday, waiting);

        (s.CarriedIn, s.OpenAtEnd).ShouldBe((0, 0));
    }

    [Fact]
    public void Planned_minutes_fall_back_to_the_median_when_unestimated()
    {
        var estimated = new Story(Oct(7), Oct(7));
        estimated.Item.EstimateMinutes = 60;
        var bare = new Story(Oct(7), Oct(7));

        Stats(Oct(7), estimated, bare).PlannedMinutes.ShouldBe(90);
    }

    [Fact]
    public void Time_travel_sets_match_the_counts_and_never_shift_with_later_edits()
    {
        var story = new Story(Oct(5), Oct(5)).Plan(Oct(7), Oct(7), "keep_today").Complete(Oct(8));

        var sixth = DayStatsCalculator.Sets(Oct(6), [story.History()]);
        sixth.CarriedIn.ShouldContain(story.Item.Id);

        var seventh = DayStatsCalculator.Sets(Oct(7), [story.History()]);
        seventh.CarriedIn.ShouldContain(story.Item.Id);
        seventh.OpenAtEnd.ShouldContain(story.Item.Id);
    }
}
