using Noto.App.Logic;
using Noto.App.ViewModels;
using Noto.Core.Models;
using Noto.Core.Presets;

namespace Noto.App.Tests;

public sealed class TodayTests : IDisposable
{
    readonly AppFixture _app = new();

    public void Dispose() => _app.Dispose();

    static KeyChord Key(string key) => new(key);

    async Task<TodayViewModel> LoadAsync()
    {
        var vm = new TodayViewModel(_app.Services, _app.Workspace.Id);
        await vm.ReloadAsync();
        return vm;
    }

    // Creates the item `daysAgo` days back (so carry accrues), then returns to today.
    async Task<Guid> AddPastAsync(string title, int daysAgo, int? estimate = null)
    {
        _app.Clock.Advance(TimeSpan.FromDays(-daysAgo));
        var id = await _app.AddAsync(title, AppFixture.Today.AddDays(-daysAgo), estimate);
        _app.Clock.Advance(TimeSpan.FromDays(daysAgo));
        return id;
    }

    static ItemRowViewModel Row(TodayViewModel vm, string title) =>
        vm.FlatRows.Concat(vm.Sections.SelectMany(s => s.Rows)).First(r => r.Title == title);

    [Fact]
    public async Task Groups_items_into_now_planned_waiting_and_done()
    {
        var planned = await _app.AddAsync("Planned", AppFixture.Today);
        var waiting = await _app.AddAsync("Waiting", AppFixture.Today);
        var done = await _app.AddAsync("Done", AppFixture.Today);
        await _app.Services.Bus.SendAsync(new Noto.Core.Commands.StartWaiting(waiting, "Priya"));
        await _app.Services.Bus.SendAsync(new Noto.Core.Commands.CompleteItem(done));
        await _app.Services.Workspaces.SetNowAsync(_app.Workspace.Id, planned);

        var vm = await LoadAsync();

        vm.Sections.Select(s => s.Title).ShouldBe(["Now", "Planned", "Waiting on", "Done today"]);
        vm.Sections[0].Rows.Single().Title.ShouldBe("Planned");
        vm.Sections[2].Rows.Single().Title.ShouldBe("Waiting");
        vm.Sections[3].Rows.Single().Title.ShouldBe("Done");
        vm.Sections[3].IsCollapsed.ShouldBeTrue();
        vm.FlatRows.Select(r => r.Title).ShouldBe(["Planned", "Waiting"]); // collapsed Done is skipped for navigation
    }

    [Fact]
    public async Task Rows_show_carry_pressure_estimate_and_a_voiceover_label()
    {
        await AddPastAsync("Deploy v2.3", daysAgo: 4, estimate: 60);
        var vm = await LoadAsync();

        var row = Row(vm, "Deploy v2.3");
        row.CarryText.ShouldBe("4");
        row.Pressure.ShouldBe(PressureState.Hot); // honest: 3–5 is hot
        row.ShowBar.ShouldBeTrue();
        row.EstimateText.ShouldBe("~1h");
        row.Glyph.ShouldBe("planned");
        row.AutomationName.ShouldBe(
            "Deploy v2.3, needs a decision, carried 4 times, stuck, estimate 1 hour"
        );
        vm.NeedsDecision.ShouldBe(1);
        vm.BannerText.ShouldBe("1 item carried over need a decision");
    }

    [Fact]
    public async Task Stale_items_get_the_stuck_chip_and_gentle_workspaces_get_no_bar()
    {
        await AddPastAsync("Old", daysAgo: 7);
        var vm = await LoadAsync();
        Row(vm, "Old").IsStuck.ShouldBeTrue();
        Row(vm, "Old").Pressure.ShouldBe(PressureState.Stale);

        await _app.Services.Workspaces.UpdateAsync(
            _app.Workspace.Id,
            ws => ws.Pressure = Pressure.Gentle
        );
        await vm.ReloadAsync();
        Row(vm, "Old").ShowBar.ShouldBeFalse();
    }

    [Fact]
    public async Task Items_without_an_estimate_show_the_median_marked_as_an_estimate()
    {
        var done = await _app.AddAsync("Finished", AppFixture.Today, estimate: 45);
        await _app.Services.Bus.SendAsync(new Noto.Core.Commands.CompleteItem(done));
        await _app.AddAsync("Open", AppFixture.Today);

        var vm = await LoadAsync();

        Row(vm, "Open").EstimateText.ShouldBe("~45m*");
        vm.Capacity!.Footnote.ShouldNotBeNull();
    }

    [Fact]
    public async Task Keyboard_navigation_moves_focus_and_shift_extends_selection()
    {
        await _app.AddAsync("A", AppFixture.Today);
        await _app.AddAsync("B", AppFixture.Today);
        await _app.AddAsync("C", AppFixture.Today);
        var vm = await LoadAsync();

        vm.FocusedRow!.Title.ShouldBe("A");
        await vm.HandleKeyAsync(Key("j"));
        vm.FocusedRow!.Title.ShouldBe("B");
        await vm.HandleKeyAsync(Key("ArrowDown"));
        vm.FocusedRow!.Title.ShouldBe("C");
        await vm.HandleKeyAsync(Key("ArrowDown"));
        vm.FocusedRow!.Title.ShouldBe("C"); // clamped
        await vm.HandleKeyAsync(Key("k"));
        vm.FocusedRow!.Title.ShouldBe("B");

        await vm.HandleKeyAsync(KeyChord.Of("shift+k"));
        vm.SelectedRows.Select(r => r.Title).ShouldBe(["A", "B"]);
        await vm.HandleKeyAsync(KeyChord.Of("cmd+a"));
        vm.SelectedRows.Count.ShouldBe(3);
    }

    [Fact]
    public async Task X_completes_the_focused_item_and_x_again_in_done_reopens_it()
    {
        var id = await _app.AddAsync("A", AppFixture.Today);
        var vm = await LoadAsync();

        await vm.HandleKeyAsync(Key("x"));
        (await _app.GetAsync(id)).Status.ShouldBe(ItemStatus.Done);

        await vm.ReloadAsync();
        vm.Sections[3].Rows.Single().Title.ShouldBe("A");
        vm.Sections[3].IsCollapsed = false;
        vm.SetFocus(vm.Sections[3].Rows[0]);
        await vm.HandleKeyAsync(Key("x"));
        (await _app.GetAsync(id)).Status.ShouldBe(ItemStatus.Open);
    }

    [Fact]
    public async Task Every_decision_key_works_on_a_multi_selection()
    {
        await AddPastAsync("A", 2);
        await AddPastAsync("B", 2);
        var vm = await LoadAsync();
        await vm.HandleKeyAsync(Key("j"));
        await vm.HandleKeyAsync(KeyChord.Of("cmd+a"));

        await vm.HandleKeyAsync(Key("d"));
        vm.Decisions.Prompt!.Text = "mon";
        await vm.HandleKeyAsync(Key("Enter"));

        var items = await _app.Db.RunAsync(s => s.Items.ListAsync(_app.Workspace.Id));
        items.ShouldAllBe(i => i.PlannedFor == new DateOnly(2026, 10, 12));
        _app.Services.Undo.Last!.Label.ShouldBe("Deferred 2 items to Mon Oct 12");
    }

    [Fact]
    public async Task Priority_keys_estimate_prompt_someday_waiting_and_breakdown()
    {
        var id = await _app.AddAsync("A", AppFixture.Today);
        var vm = await LoadAsync();

        await vm.HandleKeyAsync(Key("3"));
        (await _app.GetAsync(id)).Priority.ShouldBe(3);

        await vm.HandleKeyAsync(Key("~"));
        vm.Decisions.Prompt!.Text = "1h30m";
        await vm.HandleKeyAsync(Key("Enter"));
        (await _app.GetAsync(id)).EstimateMinutes.ShouldBe(90);

        await vm.ReloadAsync();
        await vm.HandleKeyAsync(Key("b"));
        vm.Decisions.Prompt!.Text = "one";
        await vm.HandleKeyAsync(Key("Enter"));
        vm.Decisions.Prompt!.Error.ShouldNotBeNull(); // needs 2–5 steps
        vm.Decisions.Prompt!.Text = "one; two";
        await vm.HandleKeyAsync(Key("Enter"));
        (await _app.GetAsync(id)).IsContainer.ShouldBeTrue();

        await vm.ReloadAsync();
        vm.Sections.SelectMany(s => s.Rows).Select(r => r.Title).ShouldContain("one");
        Row(vm, "one").Breadcrumb.ShouldBe("A");
    }

    [Fact]
    public async Task Inline_edit_renames_on_enter_and_cancels_on_escape()
    {
        var id = await _app.AddAsync("Old", AppFixture.Today);
        var vm = await LoadAsync();

        await vm.HandleKeyAsync(Key("e"));
        vm.IsEditingTitle.ShouldBeTrue();
        vm.EditText.ShouldBe("Old");
        await vm.HandleKeyAsync(Key("Escape"));
        vm.IsEditingTitle.ShouldBeFalse();
        (await _app.GetAsync(id)).Title.ShouldBe("Old");

        await vm.HandleKeyAsync(Key("Enter"));
        vm.EditText = "New";
        await vm.HandleKeyAsync(Key("Enter"));
        (await _app.GetAsync(id)).Title.ShouldBe("New");
    }

    [Fact]
    public async Task N_requests_the_add_field()
    {
        var vm = await LoadAsync();
        var requested = false;
        vm.NewItemRequested += () => requested = true;
        await vm.HandleKeyAsync(Key("n"));
        requested.ShouldBeTrue();
    }

    [Fact]
    public async Task Adding_with_tokens_creates_a_planned_estimated_prioritized_item()
    {
        var vm = await LoadAsync();
        vm.Add.Text = "Write API tests ~2h !3 tomorrow";
        vm.Add.Chips.ShouldBe(["~2h", "!3", "tomorrow"]);

        (await vm.Add.SubmitAsync()).ShouldBeTrue();

        var item = (await _app.Db.RunAsync(s => s.Items.ListAsync(_app.Workspace.Id))).Single();
        (item.Title, item.EstimateMinutes, item.Priority, item.PlannedFor).ShouldBe(
            ("Write API tests", 120, 3, new DateOnly(2026, 10, 8))
        );
        vm.Add.Text.ShouldBe("");
    }

    [Fact]
    public async Task Adding_defaults_to_today_waiting_creates_waiting_and_bad_input_is_rejected()
    {
        var vm = await LoadAsync();
        vm.Add.Text = "Need keys @waiting:Priya";
        await vm.Add.SubmitAsync();
        var item = (await _app.Db.RunAsync(s => s.Items.ListAsync(_app.Workspace.Id))).Single();
        (item.Status, item.WaitingOn, item.PlannedFor).ShouldBe(
            (ItemStatus.Waiting, "Priya", AppFixture.Today)
        );

        vm.Add.Text = "~15m";
        (await vm.Add.SubmitAsync()).ShouldBeFalse();
        vm.Add.Error.ShouldBe("Add a title");

        vm.Add.Text = "x /nowhere";
        (await vm.Add.SubmitAsync()).ShouldBeFalse();
        vm.Add.Error.ShouldBe("No workspace called /nowhere");
    }

    [Fact]
    public async Task One_undo_entry_per_add_even_with_several_commands()
    {
        var vm = await LoadAsync();
        vm.Add.Text = "Thing !2 ~15m";
        await vm.Add.SubmitAsync();

        await _app.Services.Undo.UndoLastAsync();

        (await _app.Db.RunAsync(s => s.Items.ListAsync(_app.Workspace.Id))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Capacity_bar_flags_overcommit_and_applies_the_defer_suggestion()
    {
        await _app.AddAsync("Big 1", AppFixture.Today, estimate: 240);
        await _app.AddAsync("Big 2", AppFixture.Today, estimate: 240);
        var vm = await LoadAsync();

        vm.Capacity!.IsOver.ShouldBeTrue();
        vm.Capacity.Text.ShouldBe("8h / 6h");
        vm.Capacity.StatusText.ShouldBe("2h over");
        vm.Capacity.Fraction.ShouldBe(1.0);
        vm.Capacity.HasSuggestion.ShouldBeTrue();

        await vm.ApplyDeferSuggestionAsync();
        await vm.ReloadAsync();

        vm.Capacity!.IsOver.ShouldBeFalse();
        vm.Capacity.StatusText.ShouldBe("Fits");
    }

    [Fact]
    public async Task Items_unit_capacity_counts_items()
    {
        await _app.Services.Workspaces.UpdateAsync(
            _app.Workspace.Id,
            ws =>
            {
                ws.CapacityUnit = CapacityUnit.Items;
                ws.DailyCapacity = 2;
            }
        );
        for (var n = 0; n < 3; n++)
            await _app.AddAsync($"T{n}", AppFixture.Today);

        var vm = await LoadAsync();

        vm.Capacity!.Text.ShouldBe("3 items / 2 items");
        vm.Capacity.IsOver.ShouldBeTrue();
    }

    [Fact]
    public async Task Make_now_moves_the_item_to_the_now_section_and_toggles_off()
    {
        var id = await _app.AddAsync("Focus on me", AppFixture.Today);
        var vm = await LoadAsync();

        await vm.HandleKeyAsync(Key("f"));
        await vm.ReloadAsync();
        vm.Sections[0].Rows.Single().Id.ShouldBe(id);
        vm.Sections[0].Rows[0].Glyph.ShouldBe("now");
        _app.Services.Focus.IsActive.ShouldBeTrue();
        _app.Services.Focus.RemainingText.ShouldBe("25:00");

        _app.Clock.Advance(TimeSpan.FromMinutes(10));
        _app.Services.Focus.RemainingText.ShouldBe("15:00");

        await vm.HandleKeyAsync(Key("f"));
        await vm.ReloadAsync();
        vm.Sections[0].IsEmpty.ShouldBeTrue();
        _app.Services.Focus.IsActive.ShouldBeFalse();

        var events = await _app.Db.RunAsync(s => s.Events.ListForItemAsync(id));
        events.Last().Type.ShouldBe(ItemEventType.FocusStopped);
        events.Last().Data!["minutes"]!.GetValue<int>().ShouldBe(10);
    }

    [Fact]
    public async Task Relentless_pressure_pins_the_most_carried_items()
    {
        await _app.Services.Workspaces.UpdateAsync(
            _app.Workspace.Id,
            ws => BuiltInPresets.Accountability.ApplyTo(ws)
        );
        await AddPastAsync("one", 1);
        await AddPastAsync("two", 2);
        await AddPastAsync("three", 3);
        await AddPastAsync("four", 4);

        var vm = await LoadAsync();

        vm.Sections[1].Rows.Take(3).Select(r => r.Title).ShouldBe(["four", "three", "two"]);
    }

    [Fact]
    public async Task Empty_today_says_so_and_a_backlog_lists_unscheduled_and_someday()
    {
        (await LoadAsync()).IsEmpty.ShouldBeTrue();

        await _app.AddAsync("Unscheduled");
        var someday = Guid.CreateVersion7();
        await _app.Services.Bus.SendAsync(
            new Noto.Core.Commands.CreateItem(someday, _app.Workspace.Id, "Maybe", IsSomeday: true)
        );

        var backlog = new BacklogViewModel(_app.Services, _app.Workspace.Id);
        await backlog.ReloadAsync();

        backlog.Sections[0].Rows.Single().Title.ShouldBe("Unscheduled");
        backlog.Sections[1].Rows.Single().Title.ShouldBe("Maybe");

        // t pulls a backlog item into today
        await backlog.HandleKeyAsync(Key("t"));
        (await _app.Db.RunAsync(s => s.Items.ListAsync(_app.Workspace.Id)))
            .Count(i => i.PlannedFor == AppFixture.Today)
            .ShouldBe(1);
    }

    [Fact]
    public async Task Completing_the_last_subtask_completes_the_parent_in_one_undo_step()
    {
        var parent = await _app.AddAsync("Parent", AppFixture.Today);
        await _app.Services.Bus.SendAsync(new Noto.Core.Commands.BreakDown(parent, ["a", "b"]));
        var vm = await LoadAsync();

        Row(vm, "a").Item.PlannedFor.ShouldBe(AppFixture.Today);
        await vm.HandleKeyAsync(Key("x")); // completes "a"
        (await _app.GetAsync(parent)).Status.ShouldBe(ItemStatus.Open);

        var b = (await _app.Db.RunAsync(s => s.Items.ListAsync(_app.Workspace.Id))).First(i =>
            i.Title == "b"
        );
        await _app.Services.Bus.SendAsync(
            new Noto.Core.Commands.PlanItem(b.Id, null, Noto.Core.Commands.PlanKind.KeepToday)
        );
        await vm.ReloadAsync();
        vm.SetFocus(Row(vm, "b"));
        await vm.HandleKeyAsync(Key("x"));

        (await _app.GetAsync(parent)).Status.ShouldBe(ItemStatus.Done);
        await _app.Services.Undo.UndoLastAsync();
        (await _app.GetAsync(parent)).Status.ShouldBe(ItemStatus.Open);
        (await _app.GetAsync(b.Id)).Status.ShouldBe(ItemStatus.Open);
    }

    [Fact]
    public async Task Pace_forecast_appears_after_two_weeks_of_history()
    {
        for (var day = 15; day >= 1; day--)
        {
            _app.Clock.Advance(TimeSpan.FromDays(-day));
            var d = DateOnly.FromDateTime(_app.Clock.UtcNow.UtcDateTime);
            for (var n = 0; n < 3; n++)
            {
                var id = await _app.AddAsync($"h{day}-{n}", d);
                await _app.Services.Bus.SendAsync(
                    n < 2
                        ? new Noto.Core.Commands.CompleteItem(id)
                        : new Noto.Core.Commands.DropItem(id, DropReason.NotNeeded)
                ); // no leftovers carrying into the test day
            }
            _app.Clock.Advance(TimeSpan.FromDays(day));
        }
        for (var n = 0; n < 3; n++)
            await _app.AddAsync($"today-{n}", AppFixture.Today);

        var vm = await LoadAsync();

        vm.ForecastText.ShouldBe("On days like this you usually finish 2 of 3. Expect 1 to carry.");
        vm.Strip.Bars.Count.ShouldBe(14);
        vm.Strip.Bars[^2].Tooltip.ShouldContain("done 2 of 2");
    }
}
