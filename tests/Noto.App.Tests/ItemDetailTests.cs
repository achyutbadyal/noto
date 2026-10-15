using Noto.App.Logic;
using Noto.Core.Derivations;
using Noto.Core.Layouts;
using Noto.Core.Models;
using Noto.Core.Tests;

namespace Noto.App.Tests;

// The words the day planner and the hover preview use about an item and about a day.
public sealed class ItemDetailTests
{
    static readonly DateOnly Today = new(2026, 10, 14);

    static TodoItem Item(Action<TodoItem>? tweak = null)
    {
        var i = Make.Item();
        tweak?.Invoke(i);
        return i;
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "tomorrow")]
    [InlineData(-1, "yesterday")]
    [InlineData(3, "Sat")]
    [InlineData(6, "Tue")]
    [InlineData(7, "Oct 21")]
    [InlineData(-10, "Oct 4")]
    [InlineData(100, "Jan 22, 2027")]
    public void Days_are_named_relative_to_today(int offset, string expected) =>
        ItemDetail.DayWord(Today.AddDays(offset), Today).ShouldBe(expected);

    [Fact]
    public void Chips_name_the_day_part_deadline_waiting_and_repeats()
    {
        var chips = ItemDetail.Chips(
            Item(i =>
            {
                i.TimeOfDay = TimeOfDay.Afternoon;
                i.DueDate = Today.AddDays(2);
                i.Status = ItemStatus.Waiting;
                i.WaitingOn = "Priya";
                i.RecurrenceRuleId = Guid.NewGuid();
            }),
            Today,
            progress: null
        );

        chips.Select(c => c.Text).ShouldBe(["Afternoon", "Due Fri", "Waiting on Priya", "Repeats"]);
        chips[0].Kind.ShouldBe("part");
        chips[1].IsDue.ShouldBeTrue();
    }

    [Fact]
    public void A_missed_deadline_is_marked_late_in_the_warm_ramp()
    {
        var chip = ItemDetail.Chips(Item(i => i.DueDate = Today.AddDays(-2)), Today, null).Single();

        chip.Text.ShouldBe("Overdue · Oct 12");
        chip.IsLate.ShouldBeTrue();
    }

    [Fact]
    public void A_finished_task_carries_no_deadline_or_day_part_chip()
    {
        ItemDetail
            .Chips(
                Item(i =>
                {
                    i.Status = ItemStatus.Done;
                    i.DueDate = Today.AddDays(-2);
                    i.TimeOfDay = TimeOfDay.Morning;
                }),
                Today,
                null
            )
            .ShouldBeEmpty();
    }

    [Fact]
    public void Notes_preview_is_the_first_real_line_without_markdown_markers()
    {
        ItemDetail.NotesPreview("\n\n## Agenda\n- one").ShouldBe("Agenda");
        ItemDetail.NotesPreview("- [ ] call the bank").ShouldBe("call the bank");
        ItemDetail.NotesPreview("   ").ShouldBe("");
        ItemDetail.NotesPreview(null).ShouldBe("");
        ItemDetail.NotesPreview(new string('a', 300), 20).Length.ShouldBe(20);
        ItemDetail.NotesPreview(new string('a', 300), 20).ShouldEndWith("…");
    }

    [Fact]
    public void Day_titles_read_as_a_person_would_say_them()
    {
        DayAheadText.Title(Today.AddDays(1), Today).ShouldBe("Tomorrow");
        DayAheadText.Title(Today.AddDays(3), Today).ShouldBe("Saturday");
        DayAheadText.Title(Today.AddDays(12), Today).ShouldBe("Mon, Oct 26");
        DayAheadText.Name(Today.AddDays(1), Today).ShouldBe("tomorrow");
        DayAheadText.Name(Today.AddDays(3), Today).ShouldBe("Saturday");
        DayAheadText.HeaderTitle(Today.AddDays(1), Today).ShouldBe("Tomorrow · Thu, Oct 15");
        DayAheadText.HeaderTitle(Today.AddDays(3), Today).ShouldBe("Saturday · Oct 17");
        DayAheadText.HeaderTitle(Today.AddDays(10), Today).ShouldBe("Sat, Oct 24 · in 10 days");
        DayAheadText.HeaderTitle(Today.AddDays(30), Today).ShouldBe("Fri, Nov 13 · in 4 weeks");
    }

    [Theory]
    [InlineData(0, 0, "6h open")]
    [InlineData(1, 60, "A light day · 5h free")]
    [InlineData(3, 240, "A comfortable day · 2h free")]
    [InlineData(5, 330, "A full day · 30m free")]
    [InlineData(6, 420, "1h over")]
    public void The_verdict_is_honest_about_how_heavy_the_day_is(
        int count,
        int minutes,
        string expected
    )
    {
        var items = Enumerable
            .Range(0, count)
            .Select(_ =>
                Item(i =>
                {
                    i.PlannedFor = Today.AddDays(1);
                    i.EstimateMinutes = minutes / count;
                })
            )
            .ToList();

        var plan = DayAheadLayout.Build(items, Today, Today.AddDays(1), Make.Workspace(), 30);

        DayAheadText.Verdict(plan).ShouldBe(expected);
    }

    [Fact]
    public void The_headline_counts_tasks_against_the_day()
    {
        var one = DayAheadLayout.Build(
            [
                Item(i =>
                {
                    i.PlannedFor = Today.AddDays(1);
                    i.EstimateMinutes = 90;
                }),
            ],
            Today,
            Today.AddDays(1),
            Make.Workspace(),
            30
        );
        DayAheadText.Headline(one).ShouldBe("1 task · 1h 30m of 6h");

        var none = DayAheadLayout.Build([], Today, Today.AddDays(1), Make.Workspace(), 30);
        DayAheadText.Headline(none).ShouldBe("Nothing planned yet");
    }
}
