using static Noto.Core.Tests.Story;

namespace Noto.Core.Tests;

// One test per row of the table in docs/04 §4.1.
public class MetricsTests
{
    [Fact]
    public void Created_today_planned_today_is_new()
    {
        var m = new Story(Oct(7), Oct(7)).Metrics(Oct(7));
        (m.Age, m.Carry).ShouldBe((0, 0));
        m.IsNew.ShouldBeTrue();
    }

    [Fact]
    public void Created_yesterday_for_yesterday_still_open_today()
    {
        var m = new Story(Oct(6), Oct(6)).Metrics(Oct(7));
        (m.Age, m.Carry).ShouldBe((1, 1));
    }

    [Fact]
    public void Missed_days_count_even_if_the_app_was_never_opened()
    {
        var m = new Story(Oct5, Oct5).Metrics(Oct(9));
        (m.Age, m.Carry).ShouldBe((4, 4));
    }

    [Fact]
    public void Defer_stops_carry_from_growing_until_the_new_date_and_counts_a_defer()
    {
        var story = new Story(Oct(1), Oct(1)).Plan(Oct(4), Oct(7), "defer");

        var m = story.Metrics(Oct(7));
        m.Carry.ShouldBe(3); // days 2,3,4 carried before the defer took effect
        m.Defers.ShouldBe(1);
    }

    [Fact]
    public void Waiting_days_do_not_carry()
    {
        var story = new Story(Oct5, Oct5).Wait(Oct(6)).EndWait(Oct(9));

        story.Metrics(Oct(9)).Carry.ShouldBe(1); // only Oct 6, before the wait took effect
        story.Metrics(Oct(10)).Carry.ShouldBe(2); // open again at the start of Oct 10
        story.Metrics(Oct(9)).Age.ShouldBe(4);
    }

    [Fact]
    public void Someday_days_do_not_carry_but_age_grows()
    {
        var story = new Story(Oct(1), Oct(1)).Someday(Oct(2), true).Plan(Oct(23), Oct(23));

        var m = story.Metrics(Oct(24));
        m.Carry.ShouldBe(2); // Oct 2 (before someday), then Oct 24 (planned Oct 23, now past)
        m.Age.ShouldBe(23);
    }

    [Fact]
    public void Completion_freezes_age_and_carry()
    {
        var story = new Story(Oct(2), Oct(2)).Complete(Oct(7));

        story.Metrics(Oct(7)).ShouldBe(new(5, 5, 0));
        story.Metrics(Oct(20)).ShouldBe(new(5, 5, 0));
    }

    [Fact]
    public void Already_done_credits_yesterday_and_does_not_count_today()
    {
        var story = new Story(Oct5, Oct5).Complete(Oct(7), credited: Oct(6));

        var m = story.Metrics(Oct(7));
        (m.Age, m.Carry).ShouldBe((1, 1));
    }

    [Fact]
    public void Drop_freezes_and_restore_resumes_aging()
    {
        var story = new Story(Oct(1), Oct(1)).Drop(Oct(3));
        story.Metrics(Oct(10)).Age.ShouldBe(2);

        story.Restore(Oct(5));
        story.Metrics(Oct(10)).Age.ShouldBe(9);
    }

    [Fact]
    public void Undone_defer_does_not_count()
    {
        var story = new Story(Oct(1), Oct(1))
            .Plan(Oct(2), Oct(5), "defer")
            .Plan(Oct(2), Oct(1), "undo_defer");
        story.Metrics(Oct(2)).Defers.ShouldBe(0);
    }

    [Fact]
    public void Same_day_events_collapse_to_end_of_day_state()
    {
        // Planned for tomorrow then re-planned for today within the same logical day.
        var story = new Story(Oct(7), Oct(7)).Plan(Oct(7), Oct(8)).Plan(Oct(7), Oct(7));
        story.Metrics(Oct(8)).Carry.ShouldBe(1);
    }
}
