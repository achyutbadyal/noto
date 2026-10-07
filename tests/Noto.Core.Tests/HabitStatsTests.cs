using Noto.Core.Habits;
using Noto.Core.Models;
using static Noto.Core.Tests.Story;

namespace Noto.Core.Tests;

public class HabitStatsTests
{
    static RecurrenceRule Rule(string rrule = "FREQ=DAILY", int? count = null, TargetPeriod? period = null, DateOnly? start = null) => new()
    {
        Id = Guid.CreateVersion7(), WorkspaceId = Guid.CreateVersion7(), RRule = rrule, MissedBehavior = MissedBehavior.Skip,
        StartDate = start ?? Oct5, TargetCount = count, TargetPeriod = period, Template = new RuleTemplate("Run"),
    };

    static IEnumerable<TodoItem> Done(RecurrenceRule rule, params DateOnly[] days) => days.Select(d =>
    {
        var i = Make.Item(rule.WorkspaceId);
        i.RecurrenceRuleId = rule.Id; i.OccurrenceDate = d; i.Status = ItemStatus.Done;
        i.CompletedOn = d; i.CompletedAt = DateTimeOffset.UtcNow;
        return i;
    });

    [Fact]
    public void Per_occurrence_streak_counts_consecutive_done_days_and_ignores_unfinished_today()
    {
        var rule = Rule();
        var stats = HabitCalculator.Compute(rule, Done(rule, Oct(5), Oct(6), Oct(8), Oct(9), Oct(10)), Oct(11), Oct5);

        stats.CurrentStreak.ShouldBe(3);
        stats.LongestStreak.ShouldBe(3);
        (stats.ScheduledPast, stats.DonePast).ShouldBe((6, 5));
    }

    [Fact]
    public void Missing_yesterday_breaks_the_streak()
    {
        var rule = Rule();
        HabitCalculator.Compute(rule, Done(rule, Oct(5), Oct(6), Oct(7)), Oct(9), Oct5).CurrentStreak.ShouldBe(0);
    }

    [Fact]
    public void Today_done_extends_the_streak()
    {
        var rule = Rule();
        HabitCalculator.Compute(rule, Done(rule, Oct(7), Oct(8), Oct(9)), Oct(9), Oct5).CurrentStreak.ShouldBe(3);
    }

    [Fact]
    public void Heatmap_marks_done_missed_pending_and_unscheduled_days()
    {
        var rule = Rule("FREQ=WEEKLY;BYDAY=MO,WE,FR");
        var stats = HabitCalculator.Compute(rule, Done(rule, Oct(5), Oct(9)), Oct(9), Oct5);

        var byDay = stats.Heatmap.ToDictionary(c => c.Day, c => c.State);
        byDay[Oct(5)].ShouldBe(HabitDayState.Done);
        byDay[Oct(6)].ShouldBe(HabitDayState.NotScheduled);
        byDay[Oct(7)].ShouldBe(HabitDayState.Missed);
        byDay[Oct(9)].ShouldBe(HabitDayState.Done);
    }

    [Fact]
    public void Heatmap_shows_today_as_pending_not_missed()
    {
        var rule = Rule();
        HabitCalculator.Compute(rule, [], Oct(7), Oct5).Heatmap.Last().State.ShouldBe(HabitDayState.Pending);
    }

    [Fact]
    public void Flex_streak_counts_weeks_that_met_the_target_so_one_miss_does_not_erase_progress()
    {
        // 5 of 7 days: week 1 met (5 done), week 2 met (5 done, two missed days), today = Monday of week 3.
        var rule = Rule(count: 5, period: TargetPeriod.Week);
        var done = Done(rule, Oct(5), Oct(6), Oct(7), Oct(8), Oct(9), Oct(12), Oct(13), Oct(14), Oct(15), Oct(17));

        var stats = HabitCalculator.Compute(rule, done, Oct(19), Oct5);

        stats.CurrentStreak.ShouldBe(2);
        stats.LongestStreak.ShouldBe(2);
    }

    [Fact]
    public void A_failed_week_resets_current_but_not_longest()
    {
        var rule = Rule(count: 3, period: TargetPeriod.Week);
        var done = Done(rule, Oct(5), Oct(6), Oct(7), Oct(12), Oct(13), Oct(14), Oct(19));

        // Week of Oct 19 is the current one; week of Oct 12 met; so is Oct 5; today is Wed Oct 21 with 1 of 3 done: reachable.
        var stats = HabitCalculator.Compute(rule, done, Oct(21), Oct5);
        stats.CurrentStreak.ShouldBe(2);

        // On Sunday, 1 of 3 is no longer reachable: the streak breaks.
        var sunday = HabitCalculator.Compute(rule, done, Oct(25), Oct5);
        sunday.CurrentStreak.ShouldBe(0);
        sunday.LongestStreak.ShouldBe(2);
    }

    [Fact]
    public void Unfinished_current_week_that_can_still_hit_the_target_is_neutral()
    {
        var rule = Rule(count: 5, period: TargetPeriod.Week);
        var done = Done(rule, Oct(5), Oct(6), Oct(7), Oct(8), Oct(9), Oct(12));

        // Wed Oct 14 of week 2: 1 done, 5 days left including today, target 5 still reachable.
        HabitCalculator.Compute(rule, done, Oct(14), Oct5).CurrentStreak.ShouldBe(1);
    }

    [Fact]
    public void Current_period_that_meets_the_target_adds_to_the_streak()
    {
        var rule = Rule(count: 2, period: TargetPeriod.Week);
        var done = Done(rule, Oct(5), Oct(6), Oct(12), Oct(13));
        HabitCalculator.Compute(rule, done, Oct(14), Oct5).CurrentStreak.ShouldBe(2);
    }

    [Fact]
    public void Monthly_targets()
    {
        var rule = Rule(count: 3, period: TargetPeriod.Month, start: new DateOnly(2026, 9, 1));
        var done = Done(rule, new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 20),
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 3));

        HabitCalculator.Compute(rule, done, new DateOnly(2026, 10, 15), new DateOnly(2026, 9, 1)).CurrentStreak.ShouldBe(2);
    }

    [Fact]
    public void Deleted_instances_do_not_count()
    {
        var rule = Rule();
        var items = Done(rule, Oct(7)).ToList();
        items[0].DeletedAt = DateTimeOffset.UtcNow;
        HabitCalculator.Compute(rule, items, Oct(8), Oct5).CurrentStreak.ShouldBe(0);
    }
}
