using Noto.Core.Commands;
using Noto.Core.Insights;
using Noto.Core.Layouts;
using Noto.Core.Models;
using static Noto.Core.Tests.Story;

namespace Noto.Core.Tests;

public class BoardLayoutTests
{
    static readonly DateOnly Today = Oct(7);
    static readonly BoardSettings Settings = new([new("design", "Design", WipLimit: 1), new("build", "Build")]);

    static TodoItem Item(Action<TodoItem>? tweak = null)
    {
        var i = Make.Item();
        tweak?.Invoke(i);
        return i;
    }

    static IReadOnlyList<Guid> Ids(IReadOnlyList<BoardColumn> cols, string id) => cols.Single(c => c.Id == id).Items.Select(i => i.Id).ToList();

    [Fact]
    public void Columns_are_fixed_with_user_columns_between_backlog_and_today()
    {
        BoardLayout.Build([], Today, Settings).Select(c => c.Id)
            .ShouldBe(["someday", "backlog", "design", "build", "today", "waiting", "done"]);
    }

    [Fact]
    public void Status_decides_the_column()
    {
        var someday = Item(i => i.IsSomeday = true);
        var backlog = Item();
        var future = Item(i => i.PlannedFor = Today.AddDays(2));
        var today = Item(i => i.PlannedFor = Today);
        var carried = Item(i => i.PlannedFor = Today.AddDays(-3));
        var waiting = Item(i => { i.Status = ItemStatus.Waiting; i.WaitingOn = "x"; });
        var done = Item(i => { i.Status = ItemStatus.Done; i.CompletedOn = Today; i.CompletedAt = DateTimeOffset.UtcNow; });
        var dropped = Item(i => { i.Status = ItemStatus.Dropped; i.DroppedAt = DateTimeOffset.UtcNow; });

        var cols = BoardLayout.Build([someday, backlog, future, today, carried, waiting, done, dropped], Today, Settings);

        Ids(cols, "someday").ShouldBe([someday.Id]);
        Ids(cols, "backlog").ToHashSet().ShouldBe([backlog.Id, future.Id]);
        Ids(cols, "today").ToHashSet().ShouldBe([today.Id, carried.Id]);
        Ids(cols, "waiting").ShouldBe([waiting.Id]);
        Ids(cols, "done").ShouldBe([done.Id]);
    }

    [Fact]
    public void User_columns_hold_unscheduled_items_and_unknown_columns_fall_back_to_backlog()
    {
        var inDesign = Item(i => i.BoardColumn = "design");
        var orphan = Item(i => i.BoardColumn = "deleted-column");

        var cols = BoardLayout.Build([inDesign, orphan], Today, Settings);

        Ids(cols, "design").ShouldBe([inDesign.Id]);
        Ids(cols, "backlog").ShouldBe([orphan.Id]);
    }

    [Fact]
    public void Wip_limit_flags_overfull_columns()
    {
        var cols = BoardLayout.Build([Item(i => i.BoardColumn = "design"), Item(i => i.BoardColumn = "design")], Today, Settings);
        cols.Single(c => c.Id == "design").IsOverWip.ShouldBeTrue();
        cols.Single(c => c.Id == "build").IsOverWip.ShouldBeFalse();
    }

    [Fact]
    public void Done_column_shows_only_the_last_seven_days()
    {
        TodoItem DoneOn(DateOnly d) => Item(i => { i.Status = ItemStatus.Done; i.CompletedOn = d; i.CompletedAt = DateTimeOffset.UtcNow; });
        var recent = DoneOn(Today.AddDays(-6));
        var old = DoneOn(Today.AddDays(-7));

        Ids(BoardLayout.Build([recent, old], Today, Settings), "done").ShouldBe([recent.Id]);
    }

    [Fact]
    public void Containers_and_deleted_items_stay_off_the_board()
    {
        var container = Item(i => i.IsContainer = true);
        var gone = Item(i => i.DeletedAt = DateTimeOffset.UtcNow);
        BoardLayout.Build([container, gone], Today, Settings).ShouldAllBe(c => c.Items.Count == 0);
    }

    [Fact]
    public void Settings_round_trip_and_keep_other_layouts_settings()
    {
        var json = Settings.ToJson("""{"timeline":{"days":30}}""");

        var back = BoardSettings.FromJson(json);

        back.UserColumns.Select(c => (c.Id, c.WipLimit)).ShouldBe([("design", 1), ("build", (int?)null)]);
        json.ShouldContain("timeline");
        BoardSettings.FromJson(null).ShouldBe(BoardSettings.Default);
    }

    [Fact]
    public void Stuck_in_column_needs_seven_quiet_days_on_unscheduled_open_items()
    {
        var stale = new Story(Oct(1), null);
        var recentlyMoved = new Story(Oct(1), null).Raw(Oct(5), ItemEventType.ColumnChanged);
        var scheduled = new Story(Oct(1), Oct(1));

        var stuck = BoardLayout.StuckInColumn(
            new[] { stale, recentlyMoved, scheduled }.Select(s => (s.Item, (IReadOnlyList<ItemEvent>)s.Events)), Oct(8), TimeOnly.MinValue, 7);

        stuck.ShouldBe([stale.Item.Id]);
    }

    static ItemCommand[] Moves(TodoItem item, string column, string? on = null) =>
        BoardMoves.Plan(item, column, Today, on).Commands.ToArray();

    [Fact]
    public void Dropping_into_today_plans_for_today()
    {
        var item = Item();
        var cmd = Moves(item, "today").ShouldHaveSingleItem().ShouldBeOfType<PlanItem>();
        (cmd.To, cmd.Kind).ShouldBe((Today, PlanKind.Plan));
        Moves(Item(i => i.PlannedFor = Today), "today").ShouldBeEmpty();
    }

    [Fact]
    public void Dropping_into_done_completes()
    {
        Moves(Item(), "done").ShouldHaveSingleItem().ShouldBeOfType<CompleteItem>();
    }

    [Fact]
    public void Dropping_into_waiting_asks_who_first()
    {
        var item = Item();
        var ask = BoardMoves.Plan(item, "waiting", Today);
        (ask.NeedsWaitingOn, ask.Commands.Count).ShouldBe((true, 0));

        Moves(item, "waiting", "alice").ShouldHaveSingleItem().ShouldBeOfType<StartWaiting>().On.ShouldBe("alice");
    }

    [Fact]
    public void Leaving_done_reopens_first_and_leaving_waiting_ends_the_wait()
    {
        var done = Item(i => { i.Status = ItemStatus.Done; i.CompletedOn = Today; i.CompletedAt = DateTimeOffset.UtcNow; });
        Moves(done, "today").Select(c => c.GetType()).ShouldBe([typeof(ReopenItem), typeof(PlanItem)]);

        var waiting = Item(i => { i.Status = ItemStatus.Waiting; i.WaitingOn = "x"; });
        Moves(waiting, "backlog").Select(c => c.GetType()).ShouldBe([typeof(EndWaiting)]);
    }

    [Fact]
    public void Moving_to_a_user_column_unschedules_and_files_it()
    {
        var item = Item(i => i.PlannedFor = Today);
        var cmds = Moves(item, "design");
        cmds.Select(c => c.GetType()).ShouldBe([typeof(PlanItem), typeof(SetBoardColumn)]);
        cmds.OfType<SetBoardColumn>().Single().Column.ShouldBe("design");
        ((PlanItem)cmds[0]).Kind.ShouldBe(PlanKind.Unschedule);
    }

    [Fact]
    public void Moving_to_backlog_clears_the_column_and_someday_is_a_toggle()
    {
        Moves(Item(i => i.BoardColumn = "design"), "backlog").ShouldHaveSingleItem().ShouldBeOfType<SetBoardColumn>().Column.ShouldBeNull();
        Moves(Item(), "someday").ShouldHaveSingleItem().ShouldBeOfType<SetSomeday>().Value.ShouldBeTrue();
        Moves(Item(i => i.IsSomeday = true), "someday").ShouldBeEmpty();
    }

    [Fact]
    public void Moving_someday_to_waiting_clears_someday_first()
    {
        Moves(Item(i => i.IsSomeday = true), "waiting", "bob").Select(c => c.GetType()).ShouldBe([typeof(SetSomeday), typeof(StartWaiting)]);
    }
}

public class TimelineLayoutTests
{
    static readonly DateOnly Today = Oct(7);

    static TodoItem Item(DateOnly? due = null, DateOnly? planned = null, Action<TodoItem>? tweak = null)
    {
        var i = Make.Item();
        i.DueDate = due; i.PlannedFor = planned;
        tweak?.Invoke(i);
        return i;
    }

    [Fact]
    public void Overdue_is_pinned_and_oldest_first()
    {
        var older = Item(due: Oct(3));
        var newer = Item(due: Oct(5));
        var view = TimelineLayout.Build([newer, older], Today);
        view.Overdue.Select(i => i.Id).ShouldBe([older.Id, newer.Id]);
    }

    [Fact]
    public void Dated_items_sit_on_their_day_and_far_ones_in_later()
    {
        var due = Item(due: Oct(9));
        var planned = Item(planned: Oct(9));
        var far = Item(due: Oct(30));
        var view = TimelineLayout.Build([due, planned, far], Today, daysAhead: 14);

        view.Days.Count.ShouldBe(14);
        view.Days.Single(d => d.Day == Oct(9)).Items.Count.ShouldBe(2);
        view.Later.ShouldBe([far]);
    }

    [Fact]
    public void Due_date_wins_over_planned_day()
    {
        var item = Item(due: Oct(12), planned: Oct(8));
        var view = TimelineLayout.Build([item], Today);
        view.Days.Single(d => d.Day == Oct(12)).Items.ShouldHaveSingleItem();
        view.Days.Single(d => d.Day == Oct(8)).Items.ShouldBeEmpty();
    }

    [Fact]
    public void Undated_and_past_planned_items_land_in_no_date_and_nothing_is_required()
    {
        var undated = Item();
        var carried = Item(planned: Oct(3));
        var someday = Item(tweak: i => i.IsSomeday = true);
        var view = TimelineLayout.Build([undated, carried, someday], Today);
        view.NoDate.Count.ShouldBe(3);
        view.Overdue.ShouldBeEmpty();
    }

    [Fact]
    public void Done_dropped_containers_and_deleted_items_are_excluded()
    {
        var done = Item(due: Oct(9), tweak: i => { i.Status = ItemStatus.Done; i.CompletedOn = Oct(7); i.CompletedAt = DateTimeOffset.UtcNow; });
        var container = Item(tweak: i => i.IsContainer = true);
        var gone = Item(due: Oct(9), tweak: i => i.DeletedAt = DateTimeOffset.UtcNow);
        var view = TimelineLayout.Build([done, container, gone], Today);
        (view.Days.Sum(d => d.Items.Count) + view.NoDate.Count + view.Overdue.Count).ShouldBe(0);
    }

    [Fact]
    public void Waiting_items_are_still_shown()
    {
        var waiting = Item(due: Oct(1), tweak: i => { i.Status = ItemStatus.Waiting; i.WaitingOn = "x"; });
        TimelineLayout.Build([waiting], Today).Overdue.ShouldHaveSingleItem();
    }
}

public class WeeklyReviewTests
{
    static readonly DateOnly Today = Oct(9); // Friday of the week of Oct 5
    readonly Workspace _ws = Make.Workspace();

    WeeklyReviewData Build(params Story[] stories) =>
        WeeklyReviewBuilder.Build(_ws, stories.Select(s => s.Record()).ToList(), Today, Oct5, ["Ship v2"]);

    [Fact]
    public void Wins_are_this_weeks_completions_oldest_first_with_the_finally_item_highlighted()
    {
        var finally_ = new Story(Oct(1), Oct(1)).Complete(Oct(8));
        var quick = new Story(Oct(8), Oct(8)).Complete(Oct(8));
        var lastWeek = new Story(Oct(1), Oct(1)).Complete(Oct(2));

        var data = Build(finally_, quick, lastWeek);

        data.Wins.Select(w => w.Item.Id).ShouldBe([finally_.Item.Id, quick.Item.Id]);
        data.Finally!.Age.ShouldBe(7);
    }

    [Fact]
    public void Someday_sweep_preselects_items_untouched_for_sixty_days()
    {
        var ancient = new Story(new DateOnly(2026, 6, 1), null, someday: true);
        var fresh = new Story(Oct(2), null, someday: true);

        var sweep = Build(ancient, fresh).SomedaySweep;

        sweep.Select(s => s.Item.Id).ShouldBe([ancient.Item.Id, fresh.Item.Id]);
        sweep[0].PreselectDrop.ShouldBeTrue();
        sweep[1].PreselectDrop.ShouldBeFalse();
    }

    [Fact]
    public void Touching_a_someday_item_resets_its_untouched_clock()
    {
        var touched = new Story(new DateOnly(2026, 6, 1), null, someday: true).Raw(Oct(1), ItemEventType.NotesChanged);
        Build(touched).SomedaySweep.Single().PreselectDrop.ShouldBeFalse();
    }

    [Fact]
    public void Stuck_patterns_suggest_the_fix_for_the_top_reason()
    {
        var story = new Story(Oct(1), Oct(1)).Stuck(Oct(6), "too_big").Stuck(Oct(7), "too_big").Stuck(Oct(8), "blocked");

        var stuck = Build(story).Stuck;

        stuck.TopReason.ShouldBe("too_big");
        stuck.Suggestion!.Kind.ShouldBe(StuckFixKind.BreakDown);
    }

    [Fact]
    public void Empty_week_has_no_suggestion_and_carries_next_weeks_outcomes()
    {
        var data = Build();
        data.Stuck.Suggestion.ShouldBeNull();
        data.Wins.ShouldBeEmpty();
        data.Finally.ShouldBeNull();
        data.NextWeekOutcomes.ShouldBe(["Ship v2"]);
    }
}
