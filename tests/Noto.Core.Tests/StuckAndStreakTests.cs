using Noto.Core.Commands;
using Noto.Core.Derivations;
using Noto.Core.Insights;
using Noto.Core.Models;
using Noto.Core.Presets;
using static Noto.Core.Tests.Story;

namespace Noto.Core.Tests;

public class StuckPromptTests
{
    [Theory]
    [InlineData(Pressure.Honest, 3, 0, true)]
    [InlineData(Pressure.Honest, 2, 3, true)]
    [InlineData(Pressure.Honest, 2, 2, false)]
    [InlineData(Pressure.Gentle, 13, 4, false)]
    [InlineData(Pressure.Gentle, 14, 0, true)]
    [InlineData(Pressure.Relentless, 2, 0, true)]
    public void Prompt_triggers_at_the_pressure_thresholds(
        Pressure level,
        int carry,
        int defers,
        bool expected
    ) =>
        StuckPrompt
            .ShouldPrompt(new ItemMetrics(carry, carry, defers), PressureThresholds.For(level))
            .ShouldBe(expected);

    [Theory]
    [InlineData(StuckReason.TooBig, StuckFixKind.BreakDown)]
    [InlineData(StuckReason.Blocked, StuckFixKind.MoveToWaiting)]
    [InlineData(StuckReason.Unclear, StuckFixKind.RewriteNextAction)]
    [InlineData(StuckReason.DontWant, StuckFixKind.MakeNowOrScheduleTomorrow)]
    [InlineData(StuckReason.NotNeeded, StuckFixKind.Drop)]
    public void Each_reason_offers_a_concrete_fix(StuckReason reason, StuckFixKind kind) =>
        StuckPrompt.FixFor(reason).Kind.ShouldBe(kind);
}

public class CompletionStreakTests
{
    [Fact]
    public void Counts_back_from_the_newest_finished_day_until_a_miss()
    {
        var days = new[]
        {
            Days.Stats(Oct(1), 3, 0),
            Days.Stats(Oct(2), 2, 1),
            Days.Stats(Oct(3), 4, 0),
            Days.Stats(Oct(4), 1, 0),
        };
        CompletionStreak.Current(days).ShouldBe(2);
        CompletionStreak.Longest(days).ShouldBe(2);
    }

    [Fact]
    public void Days_with_nothing_planned_do_not_break_the_streak()
    {
        var days = new[]
        {
            Days.Stats(Oct(1), 2, 0),
            Days.Stats(Oct(2), 0, 0),
            Days.Stats(Oct(3), 1, 0),
        };
        CompletionStreak.Current(days).ShouldBe(2);
    }

    [Fact]
    public void Input_order_does_not_matter()
    {
        var days = new[]
        {
            Days.Stats(Oct(3), 1, 0),
            Days.Stats(Oct(1), 1, 1),
            Days.Stats(Oct(2), 1, 0),
        };
        CompletionStreak.Current(days).ShouldBe(2);
    }

    [Fact]
    public void Longest_can_be_earlier_than_current()
    {
        var days = new[]
        {
            Days.Stats(Oct(1), 1, 0),
            Days.Stats(Oct(2), 1, 0),
            Days.Stats(Oct(3), 1, 0),
            Days.Stats(Oct(4), 0, 2),
            Days.Stats(Oct(5), 1, 0),
        };
        (CompletionStreak.Current(days), CompletionStreak.Longest(days)).ShouldBe((1, 3));
    }
}

public class PaceForecastTests
{
    static IEnumerable<DayStats> History(int days, int done, int load = 8) =>
        Enumerable
            .Range(0, days)
            .Select(n =>
                Days.Stats(Oct(1).AddDays(n), done, load - done, carried: 2, planned: load - 2)
            );

    [Fact]
    public void Needs_two_weeks_of_history()
    {
        PaceForecaster.Forecast(Oct(30), 8, History(13, 5)).ShouldBeNull();
        PaceForecaster.Forecast(Oct(30), 8, History(14, 5)).ShouldNotBeNull();
    }

    [Fact]
    public void Forecasts_the_median_done_on_similar_days()
    {
        var f = PaceForecaster.Forecast(Oct(30), 8, History(20, 5))!;
        (f.ExpectedDone, f.ExpectedCarry, f.Planned).ShouldBe((5, 3, 8));
        f.SampleDays.ShouldBe(10);
    }

    [Fact]
    public void Prefers_days_with_a_similar_load()
    {
        var light = History(15, 2, load: 3);
        var heavy = History(15, 6, load: 10).Select(d => d with { Day = d.Day.AddDays(20) });
        PaceForecaster
            .Forecast(Oct(1).AddDays(60), 10, light.Concat(heavy))!
            .ExpectedDone.ShouldBe(6);
    }

    [Fact]
    public void Never_expects_more_done_than_planned()
    {
        PaceForecaster.Forecast(Oct(30), 2, History(20, 7))!.ExpectedDone.ShouldBe(2);
    }

    [Fact]
    public void Ignores_empty_days_and_the_future()
    {
        var empty = Enumerable.Range(0, 20).Select(n => Days.Stats(Oct(1).AddDays(n), 0, 0));
        PaceForecaster.Forecast(Oct(30), 5, empty).ShouldBeNull();
        PaceForecaster.Forecast(Oct(5), 8, History(20, 5)).ShouldBeNull();
    }
}

public class ContainerRulesTests
{
    static TodoItem Child(ItemStatus status, DateOnly? completedOn = null)
    {
        var i = Make.Item();
        i.Status = status;
        if (status == ItemStatus.Done)
        {
            i.CompletedOn = completedOn ?? Oct(7);
            i.CompletedAt = DateTimeOffset.UtcNow;
        }
        if (status == ItemStatus.Dropped)
            i.DroppedAt = DateTimeOffset.UtcNow;
        if (status == ItemStatus.Waiting)
            i.WaitingOn = "x";
        return i;
    }

    [Fact]
    public void Progress_counts_done_over_non_dropped()
    {
        var kids = new[]
        {
            Child(ItemStatus.Done),
            Child(ItemStatus.Open),
            Child(ItemStatus.Dropped),
            Child(ItemStatus.Done),
        };
        ContainerRules.Progress(kids).ToString().ShouldBe("2/3");
    }

    [Fact]
    public void Open_or_waiting_children_keep_the_parent_open()
    {
        ContainerRules
            .Evaluate([Child(ItemStatus.Done), Child(ItemStatus.Open)])
            .ShouldBe(ContainerOutcome.StillOpen);
        ContainerRules
            .Evaluate([Child(ItemStatus.Done), Child(ItemStatus.Waiting)])
            .ShouldBe(ContainerOutcome.StillOpen);
        ContainerRules.Evaluate([]).ShouldBe(ContainerOutcome.StillOpen);
    }

    [Fact]
    public void Last_open_child_done_completes_the_parent_even_with_dropped_siblings()
    {
        ContainerRules
            .Evaluate([Child(ItemStatus.Done), Child(ItemStatus.Dropped)])
            .ShouldBe(ContainerOutcome.AutoComplete);
    }

    [Fact]
    public void All_children_dropped_asks_done_or_drop()
    {
        ContainerRules
            .Evaluate([Child(ItemStatus.Dropped), Child(ItemStatus.Dropped)])
            .ShouldBe(ContainerOutcome.AskDoneOrDrop);
    }

    [Fact]
    public void Parent_is_credited_to_the_latest_child_completion_day()
    {
        ContainerRules
            .CompletionDay([
                Child(ItemStatus.Done, Oct(5)),
                Child(ItemStatus.Done, Oct(7)),
                Child(ItemStatus.Dropped),
            ])
            .ShouldBe(Oct(7));
    }
}

public class CapacityBarTests
{
    static readonly DateOnly Today = Oct(7);

    [Fact]
    public void Under_capacity_suggests_nothing()
    {
        var ws = Make.Workspace();
        var items = new[] { Planned(ws, 60), Planned(ws, 60) };
        var view = TodayQuery.Build(items, ws, Today, _ => new ItemMetrics(0, 0, 0));

        var bar = CapacityBar.Compute(ws, view, _ => new ItemMetrics(0, 0, 0), 30);

        (bar.Committed, bar.Capacity, bar.IsOver).ShouldBe((120, 360, false));
        bar.SuggestedDefers.ShouldBeEmpty();
    }

    [Fact]
    public void Over_capacity_suggests_the_two_lowest_priority_least_carried()
    {
        var ws = Make.Workspace();
        var keep = Planned(ws, 240, priority: 3);
        var lowFresh = Planned(ws, 120, priority: 0);
        var lowCarried = Planned(ws, 120, priority: 0);
        var mid = Planned(ws, 60, priority: 2);
        Func<TodoItem, ItemMetrics> metrics = i => new(0, i == lowCarried ? 4 : 0, 0);
        var view = TodayQuery.Build([keep, lowFresh, lowCarried, mid], ws, Today, metrics);

        var bar = CapacityBar.Compute(ws, view, metrics, 30);

        bar.Over.ShouldBe(180);
        bar.SuggestedDefers.Select(i => i.Id).ShouldBe([lowFresh.Id, lowCarried.Id]);
    }

    [Fact]
    public void Unestimated_items_use_the_fallback_and_item_units_count_one_each()
    {
        var ws = Make.Workspace();
        var view = TodayQuery.Build(
            [Planned(ws, null), Planned(ws, null)],
            ws,
            Today,
            _ => new ItemMetrics(0, 0, 0)
        );
        CapacityBar.Compute(ws, view, _ => new ItemMetrics(0, 0, 0), 45).Committed.ShouldBe(90);

        ws.CapacityUnit = CapacityUnit.Items;
        ws.DailyCapacity = 1;
        var bar = CapacityBar.Compute(ws, view, _ => new ItemMetrics(0, 0, 0), 45);
        (bar.Committed, bar.IsOver).ShouldBe((2, true));
    }

    static TodoItem Planned(Workspace ws, int? minutes, int priority = 0)
    {
        var i = Make.Item(ws.Id);
        i.PlannedFor = Today;
        i.EstimateMinutes = minutes;
        i.Priority = priority;
        return i;
    }
}
