using Noto.Core.Derivations;
using Noto.Core.Insights;
using Noto.Core.Models;
using static Noto.Core.Tests.Story;

namespace Noto.Core.Tests;

public class InsightsTests
{
    static readonly DateOnly Today = new(2026, 10, 25);
    static readonly DateOnly From = Oct(1);
    static readonly DateOnly To = Oct(25);
    readonly Workspace _ws = Make.Workspace();

    InsightsReport Report(IReadOnlyList<Story> stories) =>
        InsightsEngine.Compute(_ws, stories.Select(s => s.Record()).ToList(), stories.DayStats(From, To), Today, From, To);

    static Story Sized(int day, int estimate, int doneOffset)
    {
        var s = new Story(Oct(day), Oct(day)).Complete(Oct(day + doneOffset));
        s.Item.EstimateMinutes = estimate;
        return s;
    }

    [Fact]
    public void Nothing_surfaces_below_the_minimum_sample()
    {
        var report = Report(Enumerable.Range(1, 9).Select(d => Sized(d, 15, 0)).ToList());

        report.SizeVsCompletion.ShouldBeNull();
        report.StuckMix.ShouldBeNull();
        report.MedianCarryAtCompletion.ShouldBeNull();
    }

    [Fact]
    public void Size_vs_completion_compares_same_day_rate_and_carry_per_bucket()
    {
        var stories = Enumerable.Range(1, 10).Select(d => Sized(d, 15, 0))
            .Concat(Enumerable.Range(1, 6).Select(d => Sized(d, 180, 2))).ToList();

        var size = Report(stories).SizeVsCompletion!;

        size.Sample.ShouldBe(16);
        var small = size.Data.Single(s => s.Bucket == SizeBucket.Small);
        var large = size.Data.Single(s => s.Bucket == SizeBucket.Large);
        (small.SameDayRate, small.MeanCarry).ShouldBe((1.0, 0.0));
        (large.SameDayRate, large.MeanCarry).ShouldBe((0.0, 2.0));
    }

    [Theory]
    [InlineData(15, SizeBucket.Small)]
    [InlineData(30, SizeBucket.Small)]
    [InlineData(31, SizeBucket.Medium)]
    [InlineData(119, SizeBucket.Medium)]
    [InlineData(120, SizeBucket.Large)]
    public void Estimate_buckets(int minutes, SizeBucket bucket) => InsightsEngine.BucketOf(minutes).ShouldBe(bucket);

    [Fact]
    public void Open_items_planned_today_are_not_counted_as_failures()
    {
        var stories = Enumerable.Range(1, 10).Select(d => Sized(d, 15, 0)).ToList();
        var open = new Story(Today, Today);
        open.Item.EstimateMinutes = 15;
        stories.Add(open);

        Report(stories).SizeVsCompletion!.Sample.ShouldBe(10);
    }

    [Fact]
    public void Stuck_reason_mix_counts_events_in_the_period()
    {
        var story = new Story(Oct(1), Oct(1));
        for (var d = 2; d <= 7; d++) story.Stuck(Oct(d), "too_big");
        for (var d = 8; d <= 11; d++) story.Stuck(Oct(d), "blocked");

        var mix = Report([story]).StuckMix!;

        mix.Sample.ShouldBe(10);
        mix.Data["too_big"].ShouldBe(6);
        mix.Data["blocked"].ShouldBe(4);
    }

    [Fact]
    public void Estimate_accuracy_is_focused_over_estimated()
    {
        var stories = Enumerable.Range(1, 10).Select(d =>
        {
            var s = Sized(d, 60, 0).Focus(Oct(d), 30);
            return s;
        }).ToList();

        var acc = Report(stories).EstimateAccuracy!;

        acc.Sample.ShouldBe(10);
        acc.Data.Single().MedianRatio.ShouldBe(0.5);
    }

    [Fact]
    public void Waiting_duration_is_the_median_per_person_case_insensitively()
    {
        var stories = Enumerable.Range(1, 10).Select(d => new Story(Oct(d), Oct(d)).Wait(Oct(d)).EndWait(Oct(d + 3))).ToList();

        var wait = Report(stories).WaitingByPerson!;

        wait.Sample.ShouldBe(10);
        var bob = wait.Data.Single();
        (bob.Person, bob.Intervals, bob.MedianDays).ShouldBe(("bob", 10, 3));
    }

    [Fact]
    public void Still_waiting_counts_until_today()
    {
        var stories = Enumerable.Range(1, 10).Select(d => new Story(Oct(d), Oct(d)).Wait(Oct(20))).ToList();
        Report(stories).WaitingByPerson!.Data.Single().MedianDays.ShouldBe(5);
    }

    [Fact]
    public void Median_carry_at_completion_and_stale_count()
    {
        var done = Enumerable.Range(1, 10).Select(d => Sized(d, 15, 2)).ToList(); // each carried 2
        var old = new Story(Oct(1), Oct(1)); // still open 24 days later: stale under honest pressure
        var report = Report([.. done, old]);

        report.MedianCarryAtCompletion!.Data.ShouldBe(2);
        report.StaleCount.ShouldBe(1);
        report.OldestOpen.Single().Age.ShouldBe(24);
    }

    [Fact]
    public void Weekday_load_and_rollover_trend_come_from_day_stats()
    {
        // One item per day, done two days later: every day carries.
        var stories = Enumerable.Range(1, 14).Select(d => Sized(d, 15, 1)).ToList();

        var report = Report(stories);

        report.WeekdayLoad.ShouldNotBeNull();
        report.WeekdayLoad.Data.Count.ShouldBe(7);
        report.WeekdayLoad.Data.ShouldAllBe(w => w.MeanPlanned > 0);
        report.RolloverTrend!.Data.Count.ShouldBeGreaterThanOrEqualTo(2);
        report.RolloverTrend.Data.ShouldAllBe(w => w.Rate >= 0 && w.Rate <= 1);
    }
}
