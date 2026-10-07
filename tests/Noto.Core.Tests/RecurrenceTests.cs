using Noto.Core.Models;
using Noto.Core.Recurrence;
using static Noto.Core.Tests.Story;

namespace Noto.Core.Tests;

public class RRuleTests
{
    static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    [InlineData("")]
    [InlineData("INTERVAL=2")]
    [InlineData("FREQ=YEARLY")]
    [InlineData("FREQ=DAILY;INTERVAL=0")]
    [InlineData("FREQ=WEEKLY;BYDAY=1MO")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=0")]
    [InlineData("FREQ=DAILY;COUNT=3")]
    public void Rejects_invalid_or_unsupported_rules(string text) =>
        Should.Throw<FormatException>(() => RRule.Parse(text));

    [Fact]
    public void Daily_interval_counts_from_the_start()
    {
        var rule = RRule.Parse("FREQ=DAILY;INTERVAL=3");
        rule.Between(Oct(1), Oct(10), Oct(1)).ShouldBe([Oct(1), Oct(4), Oct(7), Oct(10)]);
    }

    [Fact]
    public void Weekly_defaults_to_the_start_weekday()
    {
        RRule
            .Parse("FREQ=WEEKLY")
            .Between(Oct(1), Oct(31), Oct(7))
            .ShouldBe([Oct(7), Oct(14), Oct(21), Oct(28)]);
    }

    [Fact]
    public void Weekly_byday_with_interval_skips_alternate_weeks()
    {
        var rule = RRule.Parse("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE");
        rule.Between(Oct5, Oct(25), Oct5).ShouldBe([Oct(5), Oct(7), Oct(19), Oct(21)]);
    }

    [Fact]
    public void Nothing_occurs_before_the_start()
    {
        RRule.Parse("FREQ=DAILY").Occurs(Oct(1), Oct(2)).ShouldBeFalse();
    }

    [Fact]
    public void Monthly_defaults_to_the_start_day_and_honours_interval()
    {
        var rule = RRule.Parse("FREQ=MONTHLY;INTERVAL=2");
        rule.Between(D(2026, 1, 1), D(2026, 12, 31), D(2026, 1, 15))
            .ShouldBe([
                D(2026, 1, 15),
                D(2026, 3, 15),
                D(2026, 5, 15),
                D(2026, 7, 15),
                D(2026, 9, 15),
                D(2026, 11, 15),
            ]);
    }

    [Fact]
    public void Monthly_days_beyond_the_month_skip_that_month_per_rfc_5545()
    {
        var rule = RRule.Parse("FREQ=MONTHLY;BYMONTHDAY=31");
        rule.Between(D(2026, 2, 1), D(2026, 4, 30), D(2026, 1, 31)).ShouldBe([D(2026, 3, 31)]);
    }

    [Fact]
    public void Negative_monthdays_count_from_the_end()
    {
        var rule = RRule.Parse("FREQ=MONTHLY;BYMONTHDAY=-1");
        rule.Between(D(2026, 2, 1), D(2026, 3, 31), D(2026, 1, 1))
            .ShouldBe([D(2026, 2, 28), D(2026, 3, 31)]);
    }

    [Fact]
    public void Multiple_monthdays()
    {
        RRule
            .Parse("FREQ=MONTHLY;BYMONTHDAY=1,15")
            .Between(D(2026, 10, 1), D(2026, 10, 31), D(2026, 10, 1))
            .ShouldBe([D(2026, 10, 1), D(2026, 10, 15)]);
    }
}

public class RecurrenceEngineTests
{
    static RecurrenceRule Rule(
        string rrule,
        MissedBehavior behavior,
        DateOnly start,
        DateOnly? end = null
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = Guid.CreateVersion7(),
            RRule = rrule,
            MissedBehavior = behavior,
            StartDate = start,
            EndDate = end,
            Template = new RuleTemplate("Standup notes", EstimateMinutes: 15, Priority: 2),
        };

    [Fact]
    public void Carry_rule_creates_today_and_only_the_most_recent_missed_occurrence()
    {
        var rule = Rule("FREQ=DAILY", MissedBehavior.Carry, Oct(1));
        RecurrenceEngine.DatesToGenerate(rule, Oct(7), _ => false).ShouldBe([Oct(6), Oct(7)]);
    }

    [Fact]
    public void Nothing_is_missed_when_the_last_occurrence_already_has_an_instance()
    {
        var rule = Rule("FREQ=DAILY", MissedBehavior.Carry, Oct(1));
        RecurrenceEngine.DatesToGenerate(rule, Oct(7), d => d == Oct(6)).ShouldBe([Oct(7)]);
    }

    [Fact]
    public void Existing_instances_including_deleted_ones_are_never_recreated()
    {
        var rule = Rule("FREQ=DAILY", MissedBehavior.Carry, Oct(1));
        RecurrenceEngine.DatesToGenerate(rule, Oct(7), _ => true).ShouldBeEmpty();
    }

    [Fact]
    public void Skip_rule_never_creates_past_occurrences()
    {
        var rule = Rule("FREQ=DAILY", MissedBehavior.Skip, Oct(1));
        RecurrenceEngine.DatesToGenerate(rule, Oct(7), _ => false).ShouldBe([Oct(7)]);
    }

    [Fact]
    public void Weekly_rule_on_a_non_occurrence_day_only_reports_the_missed_one()
    {
        var rule = Rule("FREQ=WEEKLY;BYDAY=MO", MissedBehavior.Carry, Oct(1));
        RecurrenceEngine.DatesToGenerate(rule, Oct(7), _ => false).ShouldBe([Oct5]);
    }

    [Fact]
    public void Rules_that_have_not_started_or_have_ended_generate_nothing_new_for_today()
    {
        RecurrenceEngine
            .DatesToGenerate(Rule("FREQ=DAILY", MissedBehavior.Carry, Oct(10)), Oct(7), _ => false)
            .ShouldBeEmpty();

        var ended = Rule("FREQ=DAILY", MissedBehavior.Carry, Oct(1), end: Oct(3));
        RecurrenceEngine.DatesToGenerate(ended, Oct(7), _ => false).ShouldBe([Oct(3)]);
    }

    [Fact]
    public void Deleted_rules_generate_nothing()
    {
        var rule = Rule("FREQ=DAILY", MissedBehavior.Carry, Oct(1));
        rule.DeletedAt = DateTimeOffset.UtcNow;
        RecurrenceEngine.DatesToGenerate(rule, Oct(7), _ => false).ShouldBeEmpty();
    }

    [Fact]
    public void Missed_occurrences_are_derived_from_done_dates()
    {
        var rule = Rule("FREQ=DAILY", MissedBehavior.Skip, Oct(1));
        var done = new HashSet<DateOnly> { Oct(2), Oct(4) };
        RecurrenceEngine.Missed(rule, Oct(1), Oct(5), done.Contains).ShouldBe([Oct(1), Oct(3)]);
    }

    [Fact]
    public void Instances_are_deterministic_valid_and_start_fresh_on_their_own_day()
    {
        var rule = Rule("FREQ=DAILY", MissedBehavior.Carry, Oct(1));
        var ws = Make.Workspace(boundary: new TimeOnly(4, 0));
        var tz = TimeZoneInfo.Utc;

        var a = RecurrenceEngine.BuildInstance(rule, Oct(5), ws, tz);
        var b = RecurrenceEngine.BuildInstance(rule, Oct(5), ws, tz);

        a.Id.ShouldBe(b.Id);
        a.CreatedAt.ShouldBe(b.CreatedAt);
        a.CreatedAt.ShouldBe(DateTimeOffset.Parse("2026-10-05T04:00:00Z"));
        (a.Title, a.EstimateMinutes, a.Priority, a.PlannedFor).ShouldBe(
            ("Standup notes", 15, 2, Oct(5))
        );
        ItemInvariants.Check(a).ShouldBeEmpty();
        RecurrenceEngine.CreatedEvent(a).Id.ShouldBe(RecurrenceEngine.CreatedEvent(b).Id);
    }

    [Fact]
    public void A_carried_instance_ages_from_its_own_date()
    {
        var rule = Rule("FREQ=DAILY", MissedBehavior.Carry, Oct(1));
        var ws = Make.Workspace();
        var item = RecurrenceEngine.BuildInstance(rule, Oct(4), ws, TimeZoneInfo.Utc);
        var created = RecurrenceEngine.CreatedEvent(item);

        var metrics = Noto.Core.Derivations.MetricsCalculator.Compute(
            Noto.Core.Derivations.ItemTimeline.Build(item, [created], TimeOnly.MinValue),
            Oct(7)
        );

        (metrics.Age, metrics.Carry).ShouldBe((3, 3));
    }

    [Fact]
    public void Dst_gap_boundary_starts_the_day_at_the_next_valid_instant()
    {
        var ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var start = RecurrenceEngine.DayStart(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), ny);
        start.ShouldBe(DateTimeOffset.Parse("2026-03-08T07:30:00Z")); // 03:30 EDT
    }

    // Property: for any rule and day, generation is bounded, ordered, on-schedule and idempotent.
    [Fact]
    public void Generation_properties_hold_for_random_rules()
    {
        var rng = new Random(7);
        string[] rules =
        [
            "FREQ=DAILY",
            "FREQ=DAILY;INTERVAL=2",
            "FREQ=WEEKLY;BYDAY=MO,TH",
            "FREQ=WEEKLY;INTERVAL=2",
            "FREQ=MONTHLY",
            "FREQ=MONTHLY;BYMONTHDAY=-1",
        ];

        for (var n = 0; n < 300; n++)
        {
            var behavior = rng.Next(2) == 0 ? MissedBehavior.Carry : MissedBehavior.Skip;
            var start = Oct(1).AddDays(rng.Next(-60, 20));
            var rule = Rule(rules[rng.Next(rules.Length)], behavior, start);
            var today = Oct(1).AddDays(rng.Next(0, 90));
            var rrule = RRule.Parse(rule.RRule);

            var first = RecurrenceEngine.DatesToGenerate(rule, today, _ => false);

            first.Count.ShouldBeLessThanOrEqualTo(2);
            first.ShouldBe(first.OrderBy(d => d));
            first.ShouldAllBe(d => rrule.Occurs(d, start) && d <= today);
            if (behavior == MissedBehavior.Skip)
                first.ShouldAllBe(d => d == today);
            RecurrenceEngine.DatesToGenerate(rule, today, first.Contains).ShouldBeEmpty();
        }
    }
}
