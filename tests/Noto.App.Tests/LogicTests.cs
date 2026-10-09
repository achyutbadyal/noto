using Noto.App.Logic;
using Noto.Core.Derivations;
using Noto.Core.Models;
using Noto.Core.Text;

namespace Noto.App.Tests;

public class ItemLabelTests
{
    static readonly DateOnly Today = new(2026, 10, 7);

    static TodoItem Item(string title, DateOnly? planned, int? estimate = null)
    {
        var i = Noto.Core.Tests.Make.Item();
        i.Title = title;
        i.PlannedFor = planned;
        i.EstimateMinutes = estimate;
        return i;
    }

    [Fact]
    public void Matches_the_voiceover_example_from_the_spec()
    {
        var label = ItemLabels.Describe(
            Item("Deploy v2.3", Today, 60),
            new ItemMetrics(9, 4, 1),
            Today,
            isNow: false,
            stuck: false
        );
        label.ShouldBe("Deploy v2.3, planned, carried 4 times, estimate 1 hour");
    }

    [Theory]
    [InlineData(1, "carried 1 time")]
    [InlineData(2, "carried 2 times")]
    public void Carry_pluralization(int carry, string expected) =>
        ItemLabels
            .Describe(Item("x", Today), new ItemMetrics(carry, carry, 0), Today, false, false)
            .ShouldContain(expected);

    [Fact]
    public void Fresh_items_do_not_mention_carry_and_state_words_cover_every_state()
    {
        ItemLabels
            .Describe(Item("x", Today), new ItemMetrics(0, 0, 0), Today, false, false)
            .ShouldBe("x, planned");
        ItemLabels
            .StateWord(Item("x", Today.AddDays(-1)), Today, false)
            .ShouldBe("needs a decision");
        ItemLabels.StateWord(Item("x", Today.AddDays(2)), Today, false).ShouldBe("scheduled");
        ItemLabels.StateWord(Item("x", null), Today, false).ShouldBe("unscheduled");
        ItemLabels.StateWord(Item("x", Today), Today, true).ShouldBe("now");

        var someday = Item("x", null);
        someday.IsSomeday = true;
        ItemLabels.StateWord(someday, Today, false).ShouldBe("someday");

        var waiting = Item("x", Today);
        waiting.Status = ItemStatus.Waiting;
        waiting.WaitingOn = "Priya";
        ItemLabels.StateWord(waiting, Today, false).ShouldBe("waiting on Priya");
    }

    [Fact]
    public void Includes_stuck_priority_due_and_estimated_fallbacks()
    {
        var item = Item("Fix login", Today);
        item.Priority = 3;
        item.DueDate = new DateOnly(2026, 10, 9);

        var label = ItemLabels.Describe(
            item,
            new ItemMetrics(7, 5, 0),
            Today,
            false,
            stuck: true,
            fallbackMinutes: 45
        );

        label.ShouldBe(
            "Fix login, planned, carried 5 times, stuck, estimate about 45 minutes, priority high, due October 9"
        );
    }

    [Theory]
    [InlineData(1, "1 minute")]
    [InlineData(45, "45 minutes")]
    [InlineData(60, "1 hour")]
    [InlineData(90, "1 hour 30 minutes")]
    [InlineData(120, "2 hours")]
    public void Spoken_durations(int minutes, string expected) =>
        Duration.Spoken(minutes).ShouldBe(expected);

    [Theory]
    [InlineData(15, "15m")]
    [InlineData(60, "1h")]
    [InlineData(90, "1h 30m")]
    [InlineData(310, "5h 10m")]
    public void Short_durations(int minutes, string expected) =>
        Duration.Short(minutes).ShouldBe(expected);
}

public class KeyMapTests
{
    [Fact]
    public void No_scope_binds_the_same_chord_twice()
    {
        var dupes = KeyMap
            .All.GroupBy(b => (b.Scope, b.Chord))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        dupes.ShouldBeEmpty();
    }

    [Fact]
    public void Matches_the_documented_map()
    {
        KeyMap.Resolve(KeyScope.List, KeyChord.Of("x"))!.Action.ShouldBe(AppAction.Complete);
        KeyMap.Resolve(KeyScope.Review, KeyChord.Of("x"))!.Action.ShouldBe(AppAction.Drop);
        KeyMap.Resolve(KeyScope.List, KeyChord.Of("Backspace"))!.Action.ShouldBe(AppAction.Drop);
        KeyMap
            .Resolve(KeyScope.List, KeyChord.Of("shift+j"))!
            .Action.ShouldBe(AppAction.ExtendDown);
        KeyMap.Resolve(KeyScope.List, KeyChord.Of("3"))!.Arg.ShouldBe(3);
        KeyMap
            .Resolve(KeyScope.List, KeyChord.Of("cmd+shift+r"))!
            .Action.ShouldBe(AppAction.StartReview); // falls through to global
        KeyMap.Resolve(KeyScope.Global, KeyChord.Of("cmd+4"))!.Arg.ShouldBe(4);
        KeyMap.Resolve(KeyScope.List, KeyChord.Of("q")).ShouldBeNull();
    }

    [Fact]
    public void Avoids_the_os_conflicts_called_out_in_the_spec()
    {
        var chords = KeyMap.All.Select(b => b.Chord).ToList();
        chords.ShouldNotContain("cmd+m");
        chords.ShouldNotContain("cmd+s");
        chords.ShouldNotContain("alt+Space");
        chords.ShouldNotContain("cmd+ArrowLeft");
    }

    [Fact]
    public void Every_documented_action_has_a_binding()
    {
        foreach (
            var action in new[]
            {
                AppAction.CommandBar,
                AppAction.Defer,
                AppAction.KeepToday,
                AppAction.Someday,
                AppAction.WaitOn,
                AppAction.BreakDown,
                AppAction.MakeNow,
                AppAction.NewItem,
                AppAction.PrevDay,
                AppAction.NextDay,
                AppAction.Undo,
                AppAction.StartReview,
                AppAction.Shutdown,
                AppAction.SetPriority,
                AppAction.Help,
            }
        )
            KeyMap.All.ShouldContain(b => b.Action == action);
    }
}

public class ReviewSuggestionTests
{
    [Fact]
    public void Suggests_by_evidence_and_stays_quiet_otherwise()
    {
        var thresholds = Noto.Core.Presets.PressureThresholds.For(Pressure.Honest);
        var big = Noto.Core.Tests.Make.Item();
        big.EstimateMinutes = 180;

        ReviewSuggestions
            .For(big, new ItemMetrics(5, 3, 0), thresholds)!
            .Action.ShouldBe(AppAction.BreakDown);
        ReviewSuggestions
            .For(Noto.Core.Tests.Make.Item(), new ItemMetrics(5, 1, 3), thresholds)!
            .Action.ShouldBe(AppAction.Someday);
        ReviewSuggestions
            .For(Noto.Core.Tests.Make.Item(), new ItemMetrics(5, 3, 0), thresholds)!
            .Action.ShouldBe(AppAction.Drop);
        ReviewSuggestions
            .For(Noto.Core.Tests.Make.Item(), new ItemMetrics(1, 1, 0), thresholds)
            .ShouldBeNull();
    }
}
