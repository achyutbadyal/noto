using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Text;

namespace Noto.Core.Tests;

public class NaturalDateTests
{
    static readonly DateOnly Wed = new(2026, 10, 7);

    [Theory]
    [InlineData("today", "2026-10-07")]
    [InlineData("tomorrow", "2026-10-08")]
    [InlineData("tmrw", "2026-10-08")]
    [InlineData("fri", "2026-10-09")]
    [InlineData("Friday", "2026-10-09")]
    [InlineData("wed", "2026-10-14")] // same weekday means next week
    [InlineData("mon", "2026-10-12")]
    [InlineData("next week", "2026-10-12")]
    [InlineData("next fri", "2026-10-16")]
    [InlineData("next wed", "2026-10-14")]
    [InlineData("+3", "2026-10-10")]
    [InlineData("+3d", "2026-10-10")]
    [InlineData("+2w", "2026-10-21")]
    [InlineData("oct 12", "2026-10-12")]
    [InlineData("12 oct", "2026-10-12")]
    [InlineData("jan 5", "2027-01-05")] // already past this year
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
        var p = TokenParser.Parse(
            "Review PR #482 tomorrow ~30m !2 #backend @waiting:priya /work",
            Today
        );

        p.Title.ShouldBe("Review PR #482"); // a number after # is a reference, not a tag
        p.Tags.ShouldBe(["backend"]);
    }

    [Fact]
    public void Extracts_every_token_kind()
    {
        var p = TokenParser.Parse(
            "Write report tomorrow ~1h30m !3 #backend @waiting:priya /work",
            Today
        );

        p.Title.ShouldBe("Write report");
        p.PlannedFor.ShouldBe(new DateOnly(2026, 10, 8));
        p.EstimateMinutes.ShouldBe(90);
        p.Priority.ShouldBe(3);
        p.Tags.ShouldBe(["backend"]);
        p.WaitingOn.ShouldBe("priya");
        p.WorkspaceName.ShouldBe("work");
        p.Tokens.Select(t => t.Kind)
            .ShouldBe([
                TokenKind.Date,
                TokenKind.Size,
                TokenKind.Priority,
                TokenKind.Tag,
                TokenKind.Waiting,
                TokenKind.Workspace,
            ]);
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

        TokenParser
            .Parse("Read https://example.com/a/b", Today)
            .Title.ShouldBe("Read https://example.com/a/b");
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
        var sizes = new Dictionary<string, int>
        {
            ["s"] = 10,
            ["m"] = 30,
            ["l"] = 90,
        };
        TokenParser.Parse("x ~l", Today, sizes).EstimateMinutes.ShouldBe(90);
    }
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
