using Noto.App.Logic;
using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Presets;

namespace Noto.App.Tests;

public sealed class ShellTests : IDisposable
{
    readonly AppFixture _app = new();
    readonly ShellViewModel _shell;

    public ShellTests() => _shell = new ShellViewModel(_app.Services);

    public void Dispose() => _app.Dispose();

    Task<bool> Press(string spec, bool textFocused = false) => _shell.HandleKeyAsync(KeyChord.Of(spec), textFocused);

    async Task<Guid> AddPastAsync(string title, int daysAgo)
    {
        _app.Clock.Advance(TimeSpan.FromDays(-daysAgo));
        var id = await _app.AddAsync(title, AppFixture.Today.AddDays(-daysAgo));
        _app.Clock.Advance(TimeSpan.FromDays(daysAgo));
        return id;
    }

    [Fact]
    public async Task First_launch_asks_one_question_and_creates_workspaces_with_presets()
    {
        using var empty = new AppFixtureNoWorkspace();
        var shell = new ShellViewModel(empty.Services);
        await shell.InitializeAsync();

        shell.Page.ShouldBe(AppPage.Onboarding);
        shell.Onboarding!.Question.ShouldBe("What do you want Noto to keep honest?");

        await shell.Onboarding.ChooseAsync(OnboardingChoice.Both);
        await Task.Delay(100); // Completed re-initializes asynchronously
        await shell.InitializeAsync();

        shell.Workspaces.Select(w => w.Name).ShouldBe(["Work", "Personal"]);
        var all = await empty.Services.Workspaces.ListAsync();
        all.Select(w => w.Preset).ShouldBe(["sprint", "zen"]);
    }

    [Fact]
    public async Task Opens_on_today_with_workspace_tabs_and_header()
    {
        await _shell.InitializeAsync();

        _shell.Page.ShouldBe(AppPage.Today);
        _shell.HeaderTitle.ShouldBe("Today · Wed Oct 7");
        _shell.Workspaces.Single().IsSelected.ShouldBeTrue();
        _shell.Content.ShouldBeOfType<TodayViewModel>();
    }

    [Fact]
    public async Task Cmd_number_switches_workspaces_and_badges_count_decisions_not_open_items()
    {
        var other = await _app.Services.Workspaces.CreateAsync("Home", "🏠", BuiltInPresets.Zen, 1);
        await _app.AddAsync("planned today", AppFixture.Today);
        await AddPastAsync("carried", 2);
        await _shell.InitializeAsync();
        _shell.Page = AppPage.Today; // auto review may have opened; the tabs are what matter here

        _shell.Workspaces[0].NeedsDecision.ShouldBe(1);
        _shell.Workspaces[0].BadgeText.ShouldBe("1");
        _shell.Workspaces[1].HasBadge.ShouldBeFalse();

        await Press("cmd+2");
        _shell.Selected!.Id.ShouldBe(other.Id);
        _shell.Page.ShouldBe(AppPage.Today);
        await Press("cmd+1");
        _shell.Selected!.Name.ShouldBe("Work");
        await Press("cmd+9"); // no ninth workspace: ignored
        _shell.Selected!.Name.ShouldBe("Work");
    }

    [Fact]
    public async Task Badge_goes_quiet_outside_focus_hours()
    {
        await AddPastAsync("carried", 2);
        await _app.Services.Workspaces.UpdateAsync(_app.Workspace.Id, ws => ws.FocusHoursJson = Noto.Core.Workspaces.FocusHours.Weekdays(new TimeOnly(18, 0), new TimeOnly(22, 0)).ToJson());

        await _shell.InitializeAsync();

        _shell.Workspaces[0].NeedsDecision.ShouldBe(1);
        _shell.Workspaces[0].HasBadge.ShouldBeFalse();
    }

    [Fact]
    public async Task Morning_review_opens_itself_once_per_day_when_items_are_carried()
    {
        await AddPastAsync("carried", 2);
        await _shell.InitializeAsync();

        _shell.Page.ShouldBe(AppPage.Review);
        _shell.Review!.Entries.Count.ShouldBe(1);

        await Press("Escape"); // skip: the banner stays, the review doesn't reopen today
        await Task.Delay(50);
        _shell.Page.ShouldBe(AppPage.Today);

        await _shell.SelectWorkspaceAsync(_shell.Workspaces[0].Id);
        _shell.Page.ShouldBe(AppPage.Today);
        _shell.TodayPage!.BannerText.ShouldNotBeNull();
    }

    [Fact]
    public async Task Gentle_pressure_does_not_auto_open_the_review()
    {
        await AddPastAsync("carried", 2);
        await _app.Services.Workspaces.UpdateAsync(_app.Workspace.Id, ws => BuiltInPresets.Zen.ApplyTo(ws));
        await _shell.InitializeAsync();
        _shell.Page.ShouldBe(AppPage.Today);
    }

    [Fact]
    public async Task Relentless_pressure_cannot_skip_the_review_as_a_whole()
    {
        await AddPastAsync("carried", 2);
        await _app.Services.Workspaces.UpdateAsync(_app.Workspace.Id, ws => BuiltInPresets.Accountability.ApplyTo(ws));
        await _shell.InitializeAsync();

        await Press("Escape");
        _shell.Page.ShouldBe(AppPage.Review);
        _shell.Review!.Message.ShouldNotBeNull();

        await Press("d");
        _shell.Review.Decisions.Prompt!.Text = "+1";
        await Press("Enter");
        await Press("Escape");
        await Task.Delay(50);
        _shell.Page.ShouldBe(AppPage.Today);
    }

    [Fact]
    public async Task Cmd_z_undoes_the_last_action_and_nothing_to_undo_says_so()
    {
        var id = await _app.AddAsync("A", AppFixture.Today);
        await _shell.InitializeAsync();

        await Press("x");
        await _shell.RefreshAsync();
        (await _app.GetAsync(id)).Status.ShouldBe(ItemStatus.Done);
        _shell.Toast.IsVisible.ShouldBeTrue();
        _shell.Toast.Message.ShouldBe("Completed “A”");
        _shell.Toast.CanUndo.ShouldBeTrue();

        await Press("cmd+z");
        (await _app.GetAsync(id)).Status.ShouldBe(ItemStatus.Open);

        await Press("cmd+z");
        _shell.Toast.Message.ShouldBe("Nothing to undo");
    }

    [Fact]
    public async Task Toast_undo_button_undoes()
    {
        var id = await _app.AddAsync("A", AppFixture.Today);
        await _shell.InitializeAsync();
        await Press("x");

        await _shell.Toast.UndoAsync();

        (await _app.GetAsync(id)).Status.ShouldBe(ItemStatus.Open);
    }

    [Fact]
    public async Task Undo_expires_after_ten_minutes()
    {
        await _app.AddAsync("A", AppFixture.Today);
        await _shell.InitializeAsync();
        await Press("x");

        _app.Clock.Advance(TimeSpan.FromMinutes(11));

        _app.Services.Undo.Last.ShouldBeNull();
    }

    [Fact]
    public async Task Single_keys_stay_out_of_text_boxes_but_modifier_keys_still_work()
    {
        var id = await _app.AddAsync("A", AppFixture.Today);
        await _shell.InitializeAsync();

        (await Press("x", textFocused: true)).ShouldBeFalse();
        (await _app.GetAsync(id)).Status.ShouldBe(ItemStatus.Open);

        await Press("cmd+i", textFocused: true);
        _shell.IsInspectorOpen.ShouldBeFalse();
        await Press("cmd+b");
        _shell.IsSidebarExpanded.ShouldBeFalse();
    }

    [Fact]
    public async Task G_prefix_navigates_between_backlog_and_today()
    {
        await _shell.InitializeAsync();

        await Press("g"); await Press("b");
        _shell.Page.ShouldBe(AppPage.Backlog);
        _shell.Content.ShouldBeOfType<BacklogViewModel>();
        _shell.HeaderTitle.ShouldBe("Backlog");

        await Press("g"); await Press("t");
        _shell.Page.ShouldBe(AppPage.Today);

        await Press("g"); await Press("q"); // unknown second key cancels the prefix
        await Press("b");                   // so this is "break down", not "go to backlog"
        _shell.Page.ShouldBe(AppPage.Today);
    }

    [Fact]
    public async Task Brackets_time_travel_and_cmd_t_returns_to_today()
    {
        _app.Clock.Advance(TimeSpan.FromDays(-1));
        await _app.AddAsync("Left over yesterday", AppFixture.Today.AddDays(-1));
        var past = await _app.AddAsync("Past item", AppFixture.Today.AddDays(-1));
        await _app.Services.Bus.SendAsync(new CompleteItem(past));
        _app.Clock.Advance(TimeSpan.FromDays(1));
        await _app.AddAsync("Tomorrow thing", AppFixture.Today.AddDays(1));
        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.Today);

        await Press("[");
        _shell.Page.ShouldBe(AppPage.DayLog);
        _shell.ViewDay.ShouldBe(AppFixture.Today.AddDays(-1));
        _shell.DayLog!.IsFuture.ShouldBeFalse();
        _shell.DayLog.Sections.Single(s => s.Title == "Done").Entries.Single().Title.ShouldBe("Past item");
        _shell.DayLog.Summary.ShouldBe("1 of 2 done · 1 left over");

        await Press("]"); // back to today
        _shell.Page.ShouldBe(AppPage.Today);

        await Press("]");
        _shell.DayLog!.IsFuture.ShouldBeTrue();
        _shell.DayLog.Sections.Single().Entries.Single().Title.ShouldBe("Tomorrow thing");

        await Press("[");
        await Press("[");
        await Press("cmd+t");
        _shell.Page.ShouldBe(AppPage.Today);
        _shell.ViewDay.ShouldBeNull();
    }

    [Fact]
    public async Task Help_overlay_opens_with_question_mark_and_closes_with_escape()
    {
        await _shell.InitializeAsync();
        await Press("?");
        _shell.IsHelpOpen.ShouldBeTrue();
        (await Press("x")).ShouldBeTrue(); // swallowed while help is open
        await Press("Escape");
        _shell.IsHelpOpen.ShouldBeFalse();
        _shell.Shortcuts.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Review_is_reachable_by_shortcut_and_says_so_when_nothing_is_carried()
    {
        await _shell.InitializeAsync();
        await Press("cmd+shift+r");
        _shell.Page.ShouldBe(AppPage.Today);
        _shell.Toast.Message.ShouldBe("Nothing to review. You're all caught up.");

        await AddPastAsync("carried", 2);
        await Press("cmd+shift+r");
        _shell.Page.ShouldBe(AppPage.Review);
    }

    [Fact]
    public async Task Cmd_z_in_the_review_undoes_the_last_decision()
    {
        var id = await AddPastAsync("carried", 2);
        await _shell.InitializeAsync();
        await Press("t");
        (await _app.GetAsync(id)).PlannedFor.ShouldBe(AppFixture.Today);

        await Press("cmd+z");

        (await _app.GetAsync(id)).PlannedFor.ShouldBe(AppFixture.Today.AddDays(-2));
        _shell.Review!.Entries[0].IsDecided.ShouldBeFalse();
    }

    [Fact]
    public async Task Inspector_follows_the_focused_row_and_shows_the_three_numbers()
    {
        await AddPastAsync("Deploy", 4);
        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.Today);

        _shell.Inspector.Item!.Title.ShouldBe("Deploy");
        (_shell.Inspector.AgeText, _shell.Inspector.CarryText, _shell.Inspector.DefersText).ShouldBe(("4d", "↻4", "0"));
        _shell.Inspector.ShowStuckPrompt.ShouldBeTrue();
        _shell.Inspector.StuckPromptText.ShouldBe("This has been carried 4 times. What's in the way?");
        _shell.Inspector.Life.Select(l => l.Text).ShouldBe(["created", "carried ×4"]);
    }

    [Fact]
    public async Task Stuck_reasons_lead_to_their_fix_and_are_recorded()
    {
        var id = await AddPastAsync("Deploy", 4);
        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.Today);

        await _shell.Inspector.ChooseReasonAsync(StuckReason.TooBig);
        _shell.Inspector.Decisions.Prompt!.Kind.ShouldBe(PromptKind.BreakDown);
        _shell.Inspector.Decisions.Cancel();

        await _shell.Inspector.ChooseReasonAsync(StuckReason.Blocked);
        _shell.Inspector.Decisions.Prompt!.Kind.ShouldBe(PromptKind.WaitingOn);
        _shell.Inspector.Decisions.Cancel();

        await _shell.Inspector.ChooseReasonAsync(StuckReason.Unclear);
        _shell.Inspector.Decisions.Prompt!.Kind.ShouldBe(PromptKind.NextAction);
        _shell.Inspector.Decisions.Prompt!.Text = "Email Sam for the staging URL";
        await Press("Enter");
        (await _app.GetAsync(id)).Title.ShouldBe("Email Sam for the staging URL");

        var events = await _app.Db.RunAsync(s => s.Events.ListForItemAsync(id));
        events.Count(e => e.Type == ItemEventType.StuckReasonGiven).ShouldBe(3);
        events.ShouldContain(e => e.Type == ItemEventType.TitleChanged); // the old title is kept in history
    }

    [Fact]
    public async Task Not_needed_drops_with_the_reason_recorded()
    {
        var id = await AddPastAsync("Deploy", 4);
        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.Today);

        await _shell.Inspector.ChooseReasonAsync(StuckReason.NotNeeded);

        (await _app.GetAsync(id)).Status.ShouldBe(ItemStatus.Dropped);
    }

    [Fact]
    public async Task Inspector_saves_notes()
    {
        var id = await _app.AddAsync("A", AppFixture.Today);
        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.Today);

        _shell.Inspector.Notes = "remember the *milk*";
        await _shell.Inspector.SaveNotesAsync();

        (await _app.GetAsync(id)).Notes.ShouldBe("remember the *milk*");
    }

    [Fact]
    public async Task Changing_the_preset_in_settings_changes_the_three_controls_and_marks_custom()
    {
        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.Settings);
        var settings = _shell.Settings!;

        settings.SelectedPreset = BuiltInPresets.Accountability;
        await Task.Delay(150);
        var ws = (await _app.Services.Workspaces.GetAsync(_app.Workspace.Id))!;
        (ws.Layout, ws.SortOrderMode, ws.Pressure).ShouldBe((Layout.List, SortOrderMode.CarryDesc, Pressure.Relentless));
        ws.Preset.ShouldBe("accountability");

        settings.Pressure = Pressure.Gentle;
        await Task.Delay(150);
        (await _app.Services.Workspaces.GetAsync(_app.Workspace.Id))!.Preset.ShouldBe("accountability (custom)");
        settings.PresetLabel.ShouldBe("accountability (custom)");
    }

    [Fact]
    public async Task Settings_validate_day_boundary_and_time_zone()
    {
        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.Settings);
        var settings = _shell.Settings!;

        settings.DayBoundary = "25:99";
        await settings.ApplyDayBoundaryAsync();
        settings.Error.ShouldNotBeNull();

        settings.DayBoundary = "04:00";
        await settings.ApplyDayBoundaryAsync();
        (await _app.Services.Workspaces.GetAsync(_app.Workspace.Id))!.DayBoundary.ShouldBe(new TimeOnly(4, 0));

        settings.TimeZone = "Mars/Olympus";
        await settings.ApplyTimeZoneAsync();
        settings.Error.ShouldContain("Unknown time zone");
        settings.TimeZone = "Asia/Tokyo";
        await settings.ApplyTimeZoneAsync();
        (await _app.Services.Workspaces.GetAsync(_app.Workspace.Id))!.TimeZone.ShouldBe("Asia/Tokyo");
    }

    [Fact]
    public async Task Settings_list_platform_capabilities_with_reasons()
    {
        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.Settings);
        var capabilities = _shell.Settings!.Capabilities;
        capabilities.Count.ShouldBe(4);
        capabilities.ShouldAllBe(c => !c.IsSupported);
        capabilities[0].Text.ShouldBe("test"); // unsupported features show their reason instead of disappearing
    }

    [Fact]
    public void Appearance_persists_and_clamps()
    {
        var state = new InMemoryUiState();
        var appearance = new AppearanceViewModel(state) { Theme = ThemeChoice.Dark, Density = Density.Compact, BodySize = 40 };

        state.Get("theme").ShouldBe("Dark");
        state.Get("body-size").ShouldBe("18");
        appearance.RowHeight.ShouldBe(28);
        new AppearanceViewModel(state).Theme.ShouldBe(ThemeChoice.Dark);
    }

    [Fact]
    public async Task Shutdown_walks_three_steps_and_saves_the_note_for_tomorrows_review()
    {
        var done = await _app.AddAsync("Finished", AppFixture.Today);
        await _app.Services.Bus.SendAsync(new CompleteItem(done));
        var left = await _app.AddAsync("Unfinished", AppFixture.Today);
        await _shell.InitializeAsync();

        await Press("cmd+shift+d");
        var shutdown = _shell.Shutdown!;
        shutdown.Step.ShouldBe(ShutdownStep.Done);
        shutdown.DoneRows.Single().Title.ShouldBe("Finished");

        await Press("Enter");
        shutdown.Step.ShouldBe(ShutdownStep.NotDone);
        await Press("t"); // tomorrow
        (await _app.GetAsync(left)).PlannedFor.ShouldBe(AppFixture.Today.AddDays(1));
        shutdown.Review.IsComplete.ShouldBeTrue();

        await Press("Enter");
        shutdown.Step.ShouldBe(ShutdownStep.Note);
        shutdown.Note = "Call the plumber";
        await shutdown.FinishAsync();
        await Task.Delay(50);

        // The next morning's review shows yesterday's note.
        _app.NextDay();
        var review = new ReviewViewModel(_app.Services, _app.Workspace.Id, ReviewMode.Morning);
        await review.LoadAsync();
        review.DayNote.ShouldBe("Call the plumber");
    }

    [Fact]
    public async Task Shutdown_with_nothing_left_skips_straight_to_the_note()
    {
        await _shell.InitializeAsync();
        await Press("cmd+shift+d");
        await Press("Enter");
        _shell.Shutdown!.Step.ShouldBe(ShutdownStep.Note);
    }

    [Fact]
    public async Task Insights_say_when_there_is_not_enough_data()
    {
        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.Insights);
        _shell.Insights!.Cards.ShouldBeEmpty();
        _shell.Insights.EmptyText.ShouldContain("at least 10");
    }

    [Fact]
    public async Task New_day_while_open_shows_a_banner_toast()
    {
        await _app.AddAsync("A", AppFixture.Today);
        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.Today);

        _app.NextDay();
        await _shell.TickAsync();

        _shell.Toast.Message.ShouldBe("It's a new day. 1 item to decide.");
        _shell.Today.ShouldBe(AppFixture.Today.AddDays(1));
        _shell.HeaderTitle.ShouldBe("Today · Thu Oct 8");
    }

    [Fact]
    public async Task Tick_updates_the_focus_text()
    {
        var id = await _app.AddAsync("Deep work", AppFixture.Today);
        await _shell.InitializeAsync();
        await _app.Services.Focus.StartAsync(_app.Workspace.Id, await _app.GetAsync(id));
        _app.Clock.Advance(TimeSpan.FromMinutes(1));

        await _shell.TickAsync();

        _shell.FocusText.ShouldBe("Deep work · 24:00");
    }

    [Fact]
    public async Task Completing_the_focused_item_stops_the_timer()
    {
        var id = await _app.AddAsync("Deep work", AppFixture.Today);
        await _shell.InitializeAsync();
        await _app.Services.Focus.StartAsync(_app.Workspace.Id, await _app.GetAsync(id));
        await _shell.RefreshAsync();

        await _app.Services.Runner.RunAsync(new CompleteItem(id), "Completed");
        await _shell.RefreshAsync();

        _app.Services.Focus.IsActive.ShouldBeFalse();
    }
}

// A fixture with no workspaces, for the first-launch flow.
sealed class AppFixtureNoWorkspace : IDisposable
{
    readonly Noto.Data.SqliteUnitOfWork _db = Noto.Data.SqliteUnitOfWork.InMemory();

    public AppFixtureNoWorkspace()
    {
        var clock = new Noto.Core.Tests.FakeClock(DateTimeOffset.Parse("2026-10-07T10:00:00Z"));
        var bus = new CommandBus(_db, clock, Guid.CreateVersion7());
        var platform = new Noto.Platform.Abstractions.PlatformServices(
            new Noto.Platform.Abstractions.UnsupportedHotkey("test"), new Noto.Platform.Abstractions.InMemoryKeyring(),
            new Noto.Platform.Abstractions.UnsupportedCaptureContext("test"), new Noto.Platform.Abstractions.UnsupportedNotifications("test"),
            new Noto.Platform.Abstractions.StaticReduceMotion());
        Services = new AppServices(_db, bus, clock, _db, platform);
    }

    public AppServices Services { get; }
    public void Dispose() => _db.Dispose();
}

public sealed class FocusHoursBadgeTests : IDisposable
{
    readonly AppFixture _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Day_notes_persist_in_the_database()
    {
        await _app.Services.DayNotes.SetAsync(_app.Workspace.Id, AppFixture.Today, "Call the plumber");
        (await _app.Services.DayNotes.GetAsync(_app.Workspace.Id, AppFixture.Today)).ShouldBe("Call the plumber");
        (await _app.Services.DayNotes.GetAsync(_app.Workspace.Id, AppFixture.Today.AddDays(1))).ShouldBeNull();
    }
}

public sealed class CaptureTests : IDisposable
{
    readonly AppFixture _app = new();
    public void Dispose() => _app.Dispose();

    sealed class FakeContext(Noto.Platform.Abstractions.CaptureContext? context) : Noto.Platform.Abstractions.ICaptureContext
    {
        public Noto.Platform.Abstractions.Capability Capability => Noto.Platform.Abstractions.Capability.Supported;
        public Task<Noto.Platform.Abstractions.CaptureContext?> GetAsync() => Task.FromResult(context);
    }

    CaptureViewModel Create(Noto.Platform.Abstractions.CaptureContext? context = null)
    {
        var platform = _app.Services.Platform with { CaptureContext = new FakeContext(context) };
        var services = new AppServices(_app.Db, _app.Services.Bus, _app.Clock, _app.Db, platform);
        return new CaptureViewModel(services);
    }

    [Fact]
    public async Task Saves_a_captured_item_into_the_default_workspace_and_closes()
    {
        var vm = Create();
        await vm.PrepareAsync();
        var closed = false;
        vm.CloseRequested += () => closed = true;

        vm.Add.Text = "Call dentist tomorrow ~10m";
        (await vm.HandleKeyAsync(KeyChord.Of("Enter"))).ShouldBeTrue();

        closed.ShouldBeTrue();
        var item = (await _app.Db.RunAsync(s => s.Items.ListAsync(_app.Workspace.Id))).Single();
        (item.Title, item.EstimateMinutes, item.PlannedFor).ShouldBe(("Call dentist", 10, new DateOnly(2026, 10, 8)));
    }

    [Fact]
    public async Task Defaults_to_a_workspace_inside_its_focus_hours()
    {
        var evening = await _app.Services.Workspaces.CreateAsync("Home", "🏠", BuiltInPresets.Zen, 1);
        // Work only runs 18:00–22:00 on weekdays; the fake clock says Wednesday 10:00.
        await _app.Services.Workspaces.UpdateAsync(_app.Workspace.Id,
            ws => ws.FocusHoursJson = Noto.Core.Workspaces.FocusHours.Weekdays(new TimeOnly(18, 0), new TimeOnly(22, 0)).ToJson());

        var vm = Create();
        await vm.PrepareAsync();

        vm.Selected!.Id.ShouldBe(evening.Id);
    }

    [Fact]
    public async Task Tab_cycles_workspaces_and_keeps_the_typed_text()
    {
        await _app.Services.Workspaces.CreateAsync("Home", "🏠", BuiltInPresets.Zen, 1);
        var vm = Create();
        await vm.PrepareAsync();
        vm.Add.Text = "Buy milk";
        var first = vm.Selected;

        await vm.HandleKeyAsync(KeyChord.Of("Tab"));

        vm.Selected.ShouldNotBe(first);
        vm.Add.Text.ShouldBe("Buy milk");
    }

    [Fact]
    public async Task Offers_to_attach_the_current_page_and_links_it_on_save()
    {
        var vm = Create(new Noto.Platform.Abstractions.CaptureContext("Safari", "https://example.com/rfc-12", "RFC 12"));
        await vm.PrepareAsync();
        vm.HasAttachOffer.ShouldBeTrue();
        vm.AttachText.ShouldContain("RFC 12");

        await vm.HandleKeyAsync(KeyChord.Of("cmd+l"));
        vm.AttachPage.ShouldBeTrue();
        vm.Add.Text = "Read RFC";
        await vm.SaveAsync();

        var id = vm.Add.LastCreatedId!.Value;
        var links = await _app.Db.RunAsync(s => s.Links.ListForItemAsync(id));
        links.Single().Url.ShouldBe("https://example.com/rfc-12");
    }

    [Fact]
    public async Task No_attach_offer_for_non_browser_apps_and_blank_input_stays_open()
    {
        var vm = Create(new Noto.Platform.Abstractions.CaptureContext("Slack", null, null));
        await vm.PrepareAsync();
        vm.HasAttachOffer.ShouldBeFalse();

        var closed = false;
        vm.CloseRequested += () => closed = true;
        await vm.SaveAsync();

        closed.ShouldBeFalse();
        vm.Status.ShouldBe("Add a title");
    }

    [Fact]
    public async Task Escape_closes_without_saving()
    {
        var vm = Create();
        await vm.PrepareAsync();
        var closed = false;
        vm.CloseRequested += () => closed = true;
        vm.Add.Text = "never saved";

        await vm.HandleKeyAsync(KeyChord.Of("Escape"));

        closed.ShouldBeTrue();
        (await _app.Db.RunAsync(s => s.Items.ListAsync(_app.Workspace.Id))).ShouldBeEmpty();
    }
}
