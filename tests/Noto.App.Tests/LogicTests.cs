using Noto.App.Logic;
using Noto.Core.Derivations;
using Noto.Core.Models;

namespace Noto.App.Tests;

public class NaturalDateTests
{
    static readonly DateOnly Wed = new(2026, 10, 7);

    [Theory]
    [InlineData("today", "2026-10-07")]
    [InlineData("tomorrow", "2026-10-08")]
    [InlineData("tmrw", "2026-10-08")]
    [InlineData("fri", "2026-10-09")]
    [InlineData("Friday", "2026-10-09")]
    [InlineData("wed", "2026-10-14")]        // same weekday means next week
    [InlineData("mon", "2026-10-12")]
    [InlineData("next week", "2026-10-12")]
    [InlineData("next fri", "2026-10-16")]
    [InlineData("next wed", "2026-10-14")]
    [InlineData("+3", "2026-10-10")]
    [InlineData("+3d", "2026-10-10")]
    [InlineData("+2w", "2026-10-21")]
    [InlineData("oct 12", "2026-10-12")]
    [InlineData("12 oct", "2026-10-12")]
    [InlineData("jan 5", "2027-01-05")]       // already past this year
    [InlineData("2026-12-25", "2026-12-25")]
    public void Parses(string input, string expected)
    {
        NaturalDate.TryParse(input, Wed, out var date).ShouldBeTrue();
        date.ShouldBe(DateOnly.Parse(expected));
    }

    [Theory]
    [InlineData("")]
    [InlineData("whenever")]
    [InlineData("+x")]
    [InlineData("oct 45")]
    [InlineData("+3y")]
    public void Rejects(string input) => NaturalDate.TryParse(input, Wed, out _).ShouldBeFalse();

    [Fact]
    public void Next_weekday_on_a_sunday_uses_the_following_week()
    {
        var sunday = new DateOnly(2026, 10, 11);
        NaturalDate.TryParse("next mon", sunday, out var date).ShouldBeTrue();
        date.ShouldBe(new DateOnly(2026, 10, 12));
    }
}

public class TokenParserTests
{
    static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public void Parses_the_example_from_the_spec()
    {
        var p = TokenParser.Parse("Review PR #482 tomorrow ~30m !2 #backend @waiting:priya /work", Today);

        p.Title.ShouldBe("Review PR #482"); // a number after # is a reference, not a tag
        p.Tags.ShouldBe(["backend"]);
    }

    [Fact]
    public void Extracts_every_token_kind()
    {
        var p = TokenParser.Parse("Write report tomorrow ~1h30m !3 #backend @waiting:priya /work", Today);

        p.Title.ShouldBe("Write report");
        p.PlannedFor.ShouldBe(new DateOnly(2026, 10, 8));
        p.EstimateMinutes.ShouldBe(90);
        p.Priority.ShouldBe(3);
        p.Tags.ShouldBe(["backend"]);
        p.WaitingOn.ShouldBe("priya");
        p.WorkspaceName.ShouldBe("work");
        p.Tokens.Select(t => t.Kind).ShouldBe([TokenKind.Date, TokenKind.Size, TokenKind.Priority, TokenKind.Tag, TokenKind.Waiting, TokenKind.Workspace]);
    }

    [Theory]
    [InlineData("~15m", 15)]
    [InlineData("~2h", 120)]
    [InlineData("~1h30m", 90)]
    [InlineData("~s", 15)]
    [InlineData("~m", 60)]
    [InlineData("~l", 180)]
    public void Sizes(string token, int minutes) =>
        TokenParser.Parse($"thing {token}", Today).EstimateMinutes.ShouldBe(minutes);

    [Theory]
    [InlineData("~")]
    [InlineData("~15")]
    [InlineData("~x")]
    [InlineData("~0m")]
    public void Malformed_sizes_stay_in_the_title(string token)
    {
        var p = TokenParser.Parse($"thing {token}", Today);
        p.EstimateMinutes.ShouldBeNull();
        p.Title.ShouldBe($"thing {token}");
    }

    [Fact]
    public void Two_word_dates_and_urls()
    {
        var p = TokenParser.Parse("Plan offsite next week", Today);
        (p.Title, p.PlannedFor).ShouldBe(("Plan offsite", new DateOnly(2026, 10, 12)));

        TokenParser.Parse("Read https://example.com/a/b", Today).Title.ShouldBe("Read https://example.com/a/b");
        TokenParser.Parse("either and/or this", Today).WorkspaceName.ShouldBeNull();
    }

    [Fact]
    public void Priority_outside_1_to_4_is_title_text() =>
        TokenParser.Parse("thing !9", Today).Priority.ShouldBe(0);

    [Fact]
    public void Backspace_removes_a_whole_trailing_token_but_only_a_char_from_plain_text()
    {
        TokenParser.RemoveTrailingToken("Review PR ~30m", Today).ShouldBe("Review PR ");
        TokenParser.RemoveTrailingToken("Review PR ~30m ", Today).ShouldBe("Review PR ");
        TokenParser.RemoveTrailingToken("Review PR", Today).ShouldBe("Review P");
        TokenParser.RemoveTrailingToken("", Today).ShouldBe("");
    }

    [Fact]
    public void Custom_size_map_is_honoured()
    {
        var sizes = new Dictionary<string, int> { ["s"] = 10, ["m"] = 30, ["l"] = 90 };
        TokenParser.Parse("x ~l", Today, sizes).EstimateMinutes.ShouldBe(90);
    }
}

public class ItemLabelTests
{
    static readonly DateOnly Today = new(2026, 10, 7);

    static TodoItem Item(string title, DateOnly? planned, int? estimate = null)
    {
        var i = Noto.Core.Tests.Make.Item();
        i.Title = title; i.PlannedFor = planned; i.EstimateMinutes = estimate;
        return i;
    }

    [Fact]
    public void Matches_the_voiceover_example_from_the_spec()
    {
        var label = ItemLabels.Describe(Item("Deploy v2.3", Today, 60), new ItemMetrics(9, 4, 1), Today, isNow: false, stuck: false);
        label.ShouldBe("Deploy v2.3, planned, carried 4 times, estimate 1 hour");
    }

    [Theory]
    [InlineData(1, "carried 1 time")]
    [InlineData(2, "carried 2 times")]
    public void Carry_pluralization(int carry, string expected) =>
        ItemLabels.Describe(Item("x", Today), new ItemMetrics(carry, carry, 0), Today, false, false).ShouldContain(expected);

    [Fact]
    public void Fresh_items_do_not_mention_carry_and_state_words_cover_every_state()
    {
        ItemLabels.Describe(Item("x", Today), new ItemMetrics(0, 0, 0), Today, false, false).ShouldBe("x, planned");
        ItemLabels.StateWord(Item("x", Today.AddDays(-1)), Today, false).ShouldBe("needs a decision");
        ItemLabels.StateWord(Item("x", Today.AddDays(2)), Today, false).ShouldBe("scheduled");
        ItemLabels.StateWord(Item("x", null), Today, false).ShouldBe("unscheduled");
        ItemLabels.StateWord(Item("x", Today), Today, true).ShouldBe("now");

        var someday = Item("x", null); someday.IsSomeday = true;
        ItemLabels.StateWord(someday, Today, false).ShouldBe("someday");

        var waiting = Item("x", Today); waiting.Status = ItemStatus.Waiting; waiting.WaitingOn = "Priya";
        ItemLabels.StateWord(waiting, Today, false).ShouldBe("waiting on Priya");
    }

    [Fact]
    public void Includes_stuck_priority_due_and_estimated_fallbacks()
    {
        var item = Item("Fix login", Today);
        item.Priority = 3;
        item.DueDate = new DateOnly(2026, 10, 9);

        var label = ItemLabels.Describe(item, new ItemMetrics(7, 5, 0), Today, false, stuck: true, fallbackMinutes: 45);

        label.ShouldBe("Fix login, planned, carried 5 times, stuck, estimate about 45 minutes, priority high, due October 9");
    }

    [Theory]
    [InlineData(1, "1 minute")]
    [InlineData(45, "45 minutes")]
    [InlineData(60, "1 hour")]
    [InlineData(90, "1 hour 30 minutes")]
    [InlineData(120, "2 hours")]
    public void Spoken_durations(int minutes, string expected) => Duration.Spoken(minutes).ShouldBe(expected);

    [Theory]
    [InlineData(15, "15m")]
    [InlineData(60, "1h")]
    [InlineData(90, "1h 30m")]
    [InlineData(310, "5h 10m")]
    public void Short_durations(int minutes, string expected) => Duration.Short(minutes).ShouldBe(expected);
}

public class FuzzyMatchTests
{
    [Fact]
    public void Ranks_prefix_above_word_start_above_subsequence()
    {
        var prefix = FuzzyMatch.Score("go", "Go to Today");
        var word = FuzzyMatch.Score("today", "Go to Today");
        var scattered = FuzzyMatch.Score("gtt", "Go to Today");

        prefix.ShouldBeGreaterThan(word);
        word.ShouldBeGreaterThan(scattered);
        scattered.ShouldBeGreaterThan(0);
        FuzzyMatch.Score("zzz", "Go to Today").ShouldBe(0);
        FuzzyMatch.Score("", "anything").ShouldBeGreaterThan(0);
    }
}

public class KeyMapTests
{
    [Fact]
    public void No_scope_binds_the_same_chord_twice()
    {
        var dupes = KeyMap.All.GroupBy(b => (b.Scope, b.Chord)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        dupes.ShouldBeEmpty();
    }

    [Fact]
    public void Matches_the_documented_map()
    {
        KeyMap.Resolve(KeyScope.List, KeyChord.Of("x"))!.Action.ShouldBe(AppAction.Complete);
        KeyMap.Resolve(KeyScope.Review, KeyChord.Of("x"))!.Action.ShouldBe(AppAction.Drop);
        KeyMap.Resolve(KeyScope.List, KeyChord.Of("Backspace"))!.Action.ShouldBe(AppAction.Drop);
        KeyMap.Resolve(KeyScope.List, KeyChord.Of("shift+j"))!.Action.ShouldBe(AppAction.ExtendDown);
        KeyMap.Resolve(KeyScope.List, KeyChord.Of("3"))!.Arg.ShouldBe(3);
        KeyMap.Resolve(KeyScope.List, KeyChord.Of("cmd+shift+r"))!.Action.ShouldBe(AppAction.StartReview); // falls through to global
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
        foreach (var action in new[] { AppAction.CommandBar, AppAction.Defer, AppAction.KeepToday, AppAction.Someday, AppAction.WaitOn,
                     AppAction.BreakDown, AppAction.MakeNow, AppAction.NewItem, AppAction.PrevDay, AppAction.NextDay, AppAction.Undo,
                     AppAction.StartReview, AppAction.Shutdown, AppAction.SetPriority, AppAction.Help })
            KeyMap.All.ShouldContain(b => b.Action == action);
    }
}

public class ReviewSuggestionTests
{
    [Fact]
    public void Suggests_by_evidence_and_stays_quiet_otherwise()
    {
        var thresholds = Noto.Core.Presets.PressureThresholds.For(Pressure.Honest);
        var big = Noto.Core.Tests.Make.Item(); big.EstimateMinutes = 180;

        ReviewSuggestions.For(big, new ItemMetrics(5, 3, 0), thresholds)!.Action.ShouldBe(AppAction.BreakDown);
        ReviewSuggestions.For(Noto.Core.Tests.Make.Item(), new ItemMetrics(5, 1, 3), thresholds)!.Action.ShouldBe(AppAction.Someday);
        ReviewSuggestions.For(Noto.Core.Tests.Make.Item(), new ItemMetrics(5, 3, 0), thresholds)!.Action.ShouldBe(AppAction.Drop);
        ReviewSuggestions.For(Noto.Core.Tests.Make.Item(), new ItemMetrics(1, 1, 0), thresholds).ShouldBeNull();
    }
}
