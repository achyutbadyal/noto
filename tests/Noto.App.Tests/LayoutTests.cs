using Noto.App.Logic;
using Noto.App.ViewModels;
using Noto.Core.Commands;
using Noto.Core.Layouts;
using Noto.Core.Models;
using Noto.Core.Presets;

namespace Noto.App.Tests;

public sealed class LayoutTests : IDisposable
{
    readonly AppFixture _app = new();
    readonly ShellViewModel _shell;

    public LayoutTests() => _shell = new ShellViewModel(_app.Services);

    public void Dispose() => _app.Dispose();

    static KeyChord Key(string spec) => KeyChord.Of(spec);

    Task Layout(Preset preset) => _app.Services.Workspaces.UpdateAsync(_app.Workspace.Id, ws => preset.ApplyTo(ws));

    [Fact]
    public async Task Home_screen_follows_the_workspace_layout()
    {
        await _shell.InitializeAsync();
        _shell.Content.ShouldBeOfType<TodayViewModel>();

        foreach (var (preset, type) in new (Preset, Type)[]
                 { (BuiltInPresets.Kanban, typeof(BoardViewModel)), (BuiltInPresets.Deadline, typeof(TimelineViewModel)),
                   (BuiltInPresets.Habit, typeof(HabitGridViewModel)), (BuiltInPresets.Sprint, typeof(TodayViewModel)) })
        {
            await Layout(preset);
            await _shell.RefreshAsync();
            _shell.Content!.GetType().ShouldBe(type);
        }
    }

    [Fact]
    public async Task Switching_layouts_never_writes_to_items()
    {
        await _app.AddAsync("A", AppFixture.Today);
        var before = await _app.Db.RunAsync(async s => (await s.Events.ListForWorkspaceAsync(_app.Workspace.Id)).Count);
        await _shell.InitializeAsync();

        await Layout(BuiltInPresets.Kanban); await _shell.RefreshAsync();
        await Layout(BuiltInPresets.Habit); await _shell.RefreshAsync();

        (await _app.Db.RunAsync(async s => (await s.Events.ListForWorkspaceAsync(_app.Workspace.Id)).Count)).ShouldBe(before);
    }

    [Fact]
    public async Task Board_has_fixed_columns_and_maps_items_by_state()
    {
        await Layout(BuiltInPresets.Kanban);
        await _app.AddAsync("Unscheduled");
        await _app.AddAsync("Planned today", AppFixture.Today);
        var waiting = await _app.AddAsync("Waiting", AppFixture.Today);
        await _app.Services.Bus.SendAsync(new StartWaiting(waiting, "Priya"));
        var board = new BoardViewModel(_app.Services, _app.Workspace.Id);
        await board.ReloadAsync();

        board.Columns.Select(c => c.Title).ShouldBe(["Someday", "Backlog", "Today", "Waiting", "Done (7 days)"]);
        board.Columns[1].Rows.Single().Title.ShouldBe("Unscheduled");
        board.Columns[2].Rows.Single().Title.ShouldBe("Planned today");
        board.Columns[3].Rows.Single().Title.ShouldBe("Waiting");
    }

    [Fact]
    public async Task Moving_across_the_board_runs_ordinary_undoable_commands()
    {
        await Layout(BuiltInPresets.Kanban);
        var id = await _app.AddAsync("Task");
        var board = new BoardViewModel(_app.Services, _app.Workspace.Id);
        await board.ReloadAsync();
        board.SetFocus(board.Columns[1].Rows[0]);

        await board.HandleKeyAsync(Key("shift+ArrowRight"));    // Backlog -> Today
        (await _app.GetAsync(id)).PlannedFor.ShouldBe(AppFixture.Today);

        await board.ReloadAsync();
        board.SetFocus(board.Columns[2].Rows[0]);
        await board.HandleKeyAsync(Key("shift+ArrowRight"));    // Today -> Waiting asks for a person first
        board.Decisions.Prompt!.Kind.ShouldBe(PromptKind.WaitingOn);
        board.Decisions.Prompt.Text = "Priya";
        await board.HandleKeyAsync(Key("Enter"));
        (await _app.GetAsync(id)).Status.ShouldBe(ItemStatus.Waiting);

        await _app.Services.Undo.UndoLastAsync();
        (await _app.GetAsync(id)).Status.ShouldBe(ItemStatus.Open);
    }

    [Fact]
    public async Task Board_keys_move_focus_across_columns_skipping_empty_ones()
    {
        await Layout(BuiltInPresets.Kanban);
        await _app.AddAsync("Backlog item");
        await _app.AddAsync("Today item", AppFixture.Today);
        var board = new BoardViewModel(_app.Services, _app.Workspace.Id);
        await board.ReloadAsync();
        board.FocusedRow!.Title.ShouldBe("Backlog item");

        await board.HandleKeyAsync(Key("l"));
        board.FocusedRow!.Title.ShouldBe("Today item");
        await board.HandleKeyAsync(Key("h"));
        board.FocusedRow!.Title.ShouldBe("Backlog item");
    }

    [Fact]
    public async Task User_columns_wip_limits_and_the_override_prompt()
    {
        await Layout(BuiltInPresets.Kanban);
        var board = new BoardViewModel(_app.Services, _app.Workspace.Id);
        await board.ReloadAsync();
        board.NewColumnName = "In progress";
        board.NewColumnWip = "1";
        await board.AddColumnCommand.ExecuteAsync(null);
        await board.ReloadAsync();
        board.Columns.Select(c => c.Title).ShouldBe(["Someday", "Backlog", "In progress", "Today", "Waiting", "Done (7 days)"]);
        board.Columns[2].Rows.Count.ShouldBe(0);
        board.Columns[2].Header.ShouldBe("IN PROGRESS (0/1)");

        var a = await _app.AddAsync("A");
        var b = await _app.AddAsync("B");
        await board.ReloadAsync();
        board.SetFocus(board.Columns[1].Rows.First(r => r.Id == a));
        await board.HandleKeyAsync(Key("shift+ArrowRight"));
        await board.ReloadAsync();
        board.Columns[2].Rows.Single().Id.ShouldBe(a);

        board.SetFocus(board.Columns[1].Rows.First(r => r.Id == b));
        await board.HandleKeyAsync(Key("shift+ArrowRight"));     // full: asks first
        (await _app.GetAsync(b)).BoardColumn.ShouldBeNull();
        board.Decisions.Message!.ShouldContain("full");
        await board.HandleKeyAsync(Key("shift+ArrowRight"));     // repeating overrides
        (await _app.GetAsync(b)).BoardColumn.ShouldNotBeNull();
    }

    [Fact]
    public async Task Timeline_pins_overdue_and_keeps_undated_items_in_a_lane()
    {
        await Layout(BuiltInPresets.Deadline);
        var overdue = await _app.AddAsync("Late", AppFixture.Today);
        await _app.Services.Bus.SendAsync(new SetDueDate(overdue, AppFixture.Today.AddDays(-2)));
        var dated = await _app.AddAsync("Soon");
        await _app.Services.Bus.SendAsync(new SetDueDate(dated, AppFixture.Today.AddDays(3)));
        await _app.AddAsync("Someday maybe");

        var timeline = new TimelineViewModel(_app.Services, _app.Workspace.Id);
        await timeline.ReloadAsync();

        timeline.Sections[0].Title.ShouldBe("Overdue");
        timeline.Sections[0].Rows.Single().Title.ShouldBe("Late");
        timeline.Sections.Single(s => s.Title == AppFixture.Today.AddDays(3).ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture)).Rows.Single().Title.ShouldBe("Soon");
        timeline.Sections.Single(s => s.Title == "No date").Rows.Single().Title.ShouldBe("Someday maybe");
    }

    [Fact]
    public async Task Habit_grid_shows_skip_rules_ticks_today_and_lists_other_items()
    {
        await Layout(BuiltInPresets.Habit);
        _app.Clock.Advance(TimeSpan.FromDays(-3));
        var start = DateOnly.FromDateTime(_app.Clock.UtcNow.UtcDateTime);
        await _app.Services.Recurrence.CreateRuleAsync(_app.Workspace.Id, "FREQ=DAILY", new RuleTemplate("Morning run"), start, MissedBehavior.Skip);
        _app.Clock.Advance(TimeSpan.FromDays(3));
        await _app.AddAsync("One-off errand");

        var grid = new HabitGridViewModel(_app.Services, _app.Workspace.Id);
        await grid.ReloadAsync();

        grid.Habits.Count.ShouldBe(1);
        var habit = grid.Habits[0];
        habit.Name.ShouldBe("Morning run");
        habit.Cells.Count.ShouldBe(7);
        habit.Cells.Single(c => c.IsToday).State.ShouldBe(Noto.Core.Habits.HabitDayState.Pending);
        habit.Cells.Single(c => c.IsToday).Glyph.ShouldBe("□"); // missed/pending days are empty squares, never a red cross
        grid.DayLabels.Count.ShouldBe(7);
        grid.Sections[0].Title.ShouldBe("Not habits");
        grid.Sections[0].IsCollapsed.ShouldBeTrue();
        grid.Sections[0].Rows.Single().Title.ShouldBe("One-off errand");

        await grid.HandleKeyAsync(Key("x"));
        await grid.ReloadAsync();
        grid.Habits[0].Cells.Single(c => c.IsToday).State.ShouldBe(Noto.Core.Habits.HabitDayState.Done);
        grid.Habits[0].StreakText.ShouldContain("streak 1");

        await _app.Services.Undo.UndoLastAsync();
        await grid.ReloadAsync();
        grid.Habits[0].TodayDone.ShouldBeFalse();
    }

    [Fact]
    public async Task Today_all_spans_workspaces_with_colour_bars_and_drops_quiet_ones()
    {
        var home = await _app.Services.Workspaces.CreateAsync("Home", "🏠", BuiltInPresets.Zen, 1);
        await _app.AddAsync("Work thing", AppFixture.Today, estimate: 60);
        await _app.Services.Bus.SendAsync(new CreateItem(Guid.CreateVersion7(), home.Id, "Home thing", AppFixture.Today));
        var after = await _app.Services.Workspaces.CreateAsync("After hours", "🌙", BuiltInPresets.Zen, 2);
        await _app.Services.Workspaces.UpdateAsync(after.Id, ws => ws.FocusHoursJson = Noto.Core.Workspaces.FocusHours.Weekdays(new TimeOnly(18, 0), new TimeOnly(22, 0)).ToJson());
        await _app.Services.Bus.SendAsync(new CreateItem(Guid.CreateVersion7(), after.Id, "Night thing", AppFixture.Today));

        var all = new TodayAllViewModel(_app.Services);
        await all.ReloadAsync();

        all.Sections.Select(s => s.Title).ShouldBe(["Work", "Home"]);
        all.Sections.SelectMany(s => s.Rows).ShouldAllBe(r => r.HasWorkspaceAccent);
        all.QuietText.ShouldBe("Outside focus hours: After hours");
        all.CapacityText.ShouldStartWith("Capacity 1h 30m / 12h"); // 60m + median-less default 30m, two 6h days
        all.FocusedSnapshot!.Workspace.Name.ShouldBe("Work");
        all.SetFocus(all.Sections[1].Rows[0]);
        all.FocusedSnapshot!.Workspace.Name.ShouldBe("Home");
    }

    [Fact]
    public async Task Cmd_0_opens_today_all_and_actions_work_across_workspaces()
    {
        var home = await _app.Services.Workspaces.CreateAsync("Home", "🏠", BuiltInPresets.Zen, 1);
        var id = Guid.CreateVersion7();
        await _app.Services.Bus.SendAsync(new CreateItem(id, home.Id, "Home thing", AppFixture.Today));
        await _shell.InitializeAsync();

        await _shell.HandleKeyAsync(Key("cmd+0"));
        _shell.Page.ShouldBe(AppPage.TodayAll);
        var all = _shell.TodayAllPage!;
        all.SetFocus(all.Sections.SelectMany(s => s.Rows).First(r => r.Id == id));
        await _shell.RefreshAsync();
        _shell.Inspector.Item!.Id.ShouldBe(id); // inspector reads the row's own workspace

        await all.HandleKeyAsync(Key("x"));
        (await _app.GetAsync(id)).Status.ShouldBe(ItemStatus.Done);
    }

    [Fact]
    public async Task New_workspace_from_a_template_gets_its_preset_and_focus_hours()
    {
        await _shell.InitializeAsync();
        _shell.Templates.ShouldBe(["Work", "Personal", "Health", "Side Projects"]);

        await _shell.NewWorkspaceCommand.ExecuteAsync("Side Projects");

        _shell.Selected!.Name.ShouldBe("Side Projects");
        _shell.Content.ShouldBeOfType<BoardViewModel>(); // Kanban preset
        await _shell.NewWorkspaceCommand.ExecuteAsync("Work");
        var work = (await _app.Services.Workspaces.ListAsync()).Last();
        work.FocusHoursJson.ShouldNotBeNull();
    }

    [Fact]
    public async Task Weekly_review_walks_five_steps_applies_the_sweep_and_pins_outcomes()
    {
        // A win this week and a stale someday item.
        var win = await _app.AddAsync("Ship it", AppFixture.Today);
        await _app.Services.Bus.SendAsync(new CompleteItem(win));
        _app.Clock.Advance(TimeSpan.FromDays(-70));
        var stale = Guid.CreateVersion7();
        await _app.Services.Bus.SendAsync(new CreateItem(stale, _app.Workspace.Id, "Learn Rust", IsSomeday: true));
        _app.Clock.Advance(TimeSpan.FromDays(70));
        var fresh = Guid.CreateVersion7();
        await _app.Services.Bus.SendAsync(new CreateItem(fresh, _app.Workspace.Id, "Read book", IsSomeday: true));

        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.WeeklyReview);
        var review = _shell.WeeklyReview!;

        review.Heading.ShouldBe("Week of Oct 5");
        review.Wins.Single().Title.ShouldBe("Ship it");
        review.Sweep.First(s => s.Title == "Learn Rust").Choice.ShouldBe(SweepChoice.Drop);   // 60+ days untouched
        review.Sweep.First(s => s.Title == "Read book").Choice.ShouldBe(SweepChoice.Keep);

        for (var n = 0; n < 4; n++) review.Next();
        review.IsLast.ShouldBeTrue();
        review.Outcome1 = "Ship v2"; review.Outcome2 = "Hire"; 
        review.Sweep.First(s => s.Title == "Read book").PickCommand.Execute("Promote");
        await review.FinishAsync();
        await Task.Delay(100);

        (await _app.GetAsync(stale)).Status.ShouldBe(ItemStatus.Dropped);
        (await _app.GetAsync(fresh)).PlannedFor.ShouldBe(AppFixture.Today);
        await _app.Services.Undo.UndoLastAsync();     // the whole sweep is one undo step
        (await _app.GetAsync(stale)).Status.ShouldBe(ItemStatus.Open);

        // Outcomes are for next week; the header shows them once that week is current.
        _app.Clock.Advance(TimeSpan.FromDays(7));
        await _shell.RefreshAsync();
        _shell.WeekOutcomes.ShouldBe("This week: Ship v2 · Hire");
    }

    [Fact]
    public async Task Import_and_export_round_trip_through_settings()
    {
        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.Settings);
        var settings = _shell.Settings!;

        await settings.ImportAsync("- [ ] Buy milk\n- [x] Pay rent\n");
        settings.DataStatus.ShouldBe("Imported 2 items (1 completed)");
        var items = await _app.Db.RunAsync(s => s.Items.ListAsync(_app.Workspace.Id));
        items.Select(i => i.Title).OrderBy(t => t).ShouldBe(["Buy milk", "Pay rent"]);
        var metrics = (await _app.Services.Reader.LoadAsync(_app.Workspace.Id)).Metrics;
        metrics.Values.ShouldAllBe(m => m.Carry == 0); // imported history never counts as carry

        (await settings.ExportCsvAsync()).ShouldContain("Buy milk");
        (await settings.ExportJsonAsync()).ShouldContain("Pay rent");

        settings.ImportFormat = Noto.Core.Import.ImportFormat.TodoistJson;
        await settings.ImportAsync("not json");
        settings.DataStatus.ShouldNotBeNull();
    }
}
