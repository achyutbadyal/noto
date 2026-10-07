using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Presets;
using Noto.Core.Workspaces;

namespace Noto.Data.Tests;

public class WorkspaceServiceTests : IDisposable
{
    readonly FeatureFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Create_from_templates_applies_preset_and_focus_hours()
    {
        var work = await _f.Workspaces.CreateAsync(WorkspaceTemplates.Work);
        var side = await _f.Workspaces.CreateAsync(WorkspaceTemplates.SideProjects);

        (work.Layout, work.Pressure).ShouldBe((Layout.List, Pressure.Honest));
        FocusHours.FromJson(work.FocusHoursJson)!.Days.Count.ShouldBe(5);
        (side.Layout, side.SortOrderMode).ShouldBe((Layout.Board, SortOrderMode.Manual));
        side.SortRank.ShouldBeGreaterThan(work.SortRank);
    }

    [Fact]
    public async Task Rename_validates_and_persists()
    {
        await _f.Workspaces.RenameAsync(_f.Ws.Id, "  Day job ");
        (await _f.Uow.RunAsync(s => s.Workspaces.GetAsync(_f.Ws.Id)))!.Name.ShouldBe("Day job");
        await Should.ThrowAsync<CommandException>(() => _f.Workspaces.RenameAsync(_f.Ws.Id, " "));
    }

    [Fact]
    public async Task Reorder_sets_the_sidebar_order()
    {
        var b = await _f.Workspaces.CreateAsync(WorkspaceTemplates.Personal);
        var c = await _f.Workspaces.CreateAsync(WorkspaceTemplates.Health);

        await _f.Workspaces.ReorderAsync([c.Id, _f.Ws.Id]);

        (await _f.Workspaces.ListAsync()).Select(w => w.Id).ShouldBe([c.Id, _f.Ws.Id, b.Id]);
    }

    [Fact]
    public async Task Archive_hides_from_the_list_and_unarchive_restores()
    {
        await _f.Workspaces.ArchiveAsync(_f.Ws.Id);
        (await _f.Workspaces.ListAsync()).ShouldBeEmpty();
        (await _f.Workspaces.ListArchivedAsync()).ShouldHaveSingleItem();

        await _f.Workspaces.UnarchiveAsync(_f.Ws.Id);
        (await _f.Workspaces.ListAsync()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Soft_delete_is_recoverable_for_thirty_days_then_not()
    {
        await _f.Workspaces.DeleteAsync(_f.Ws.Id);
        (await _f.Workspaces.ListAsync()).ShouldBeEmpty();
        (await _f.Workspaces.ListRecoverableAsync()).ShouldHaveSingleItem();

        _f.Clock.Advance(TimeSpan.FromDays(29));
        await _f.Workspaces.RestoreAsync(_f.Ws.Id);
        (await _f.Workspaces.ListAsync()).ShouldHaveSingleItem();

        await _f.Workspaces.DeleteAsync(_f.Ws.Id);
        _f.Clock.Advance(TimeSpan.FromDays(31));
        (await _f.Workspaces.ListRecoverableAsync()).ShouldBeEmpty();
        await Should.ThrowAsync<CommandException>(() => _f.Workspaces.RestoreAsync(_f.Ws.Id));
    }

    [Fact]
    public async Task Deleting_a_workspace_leaves_its_items_untouched()
    {
        var id = await _f.CreateItemAsync("keep me", new DateOnly(2026, 10, 7));
        await _f.Workspaces.DeleteAsync(_f.Ws.Id);
        (await _f.LoadAsync(id)).DeletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Now_item_must_be_an_open_item_of_the_workspace()
    {
        var id = await _f.CreateItemAsync("focus", new DateOnly(2026, 10, 7));
        await _f.Workspaces.SetNowAsync(_f.Ws.Id, id);
        (await _f.Uow.RunAsync(s => s.Workspaces.GetAsync(_f.Ws.Id)))!.NowItemId.ShouldBe(id);

        await _f.Workspaces.SetNowAsync(_f.Ws.Id, null);
        (await _f.Uow.RunAsync(s => s.Workspaces.GetAsync(_f.Ws.Id)))!.NowItemId.ShouldBeNull();

        await _f.Bus.SendAsync(new CompleteItem(id));
        await Should.ThrowAsync<CommandException>(() => _f.Workspaces.SetNowAsync(_f.Ws.Id, id));

        var other = await _f.Workspaces.CreateAsync(WorkspaceTemplates.Personal);
        var open = await _f.CreateItemAsync("x");
        await Should.ThrowAsync<CommandException>(() => _f.Workspaces.SetNowAsync(other.Id, open));
    }

    [Fact]
    public async Task Changing_controls_marks_the_preset_custom_and_never_writes_items()
    {
        var id = await _f.CreateItemAsync("task", new DateOnly(2026, 10, 7));
        var before = await _f.LoadAsync(id);

        await _f.Workspaces.SetControlsAsync(_f.Ws.Id, pressure: Pressure.Relentless);
        var ws = (await _f.Uow.RunAsync(s => s.Workspaces.GetAsync(_f.Ws.Id)))!;
        ws.Preset.ShouldBe("sprint (custom)");

        await _f.Workspaces.ApplyPresetAsync(_f.Ws.Id, BuiltInPresets.Accountability);
        (await _f.Uow.RunAsync(s => s.Workspaces.GetAsync(_f.Ws.Id)))!.Preset.ShouldBe(
            "accountability"
        );

        var after = await _f.LoadAsync(id);
        (after.Title, after.PlannedFor, after.Status).ShouldBe(
            (before.Title, before.PlannedFor, before.Status)
        );
        (await _f.Uow.RunAsync(s => s.Events.ListForItemAsync(id))).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Focus_hours_capacity_and_time_settings()
    {
        await _f.Workspaces.SetFocusHoursAsync(
            _f.Ws.Id,
            FocusHours.Weekdays(new TimeOnly(8, 0), new TimeOnly(16, 0))
        );
        await _f.Workspaces.SetCapacityAsync(_f.Ws.Id, CapacityUnit.Items, 5);
        await _f.Workspaces.SetTimeAsync(_f.Ws.Id, "Asia/Tokyo", false, new TimeOnly(4, 0));

        var ws = (await _f.Uow.RunAsync(s => s.Workspaces.GetAsync(_f.Ws.Id)))!;
        (ws.CapacityUnit, ws.DailyCapacity, ws.TimeZone, ws.DayBoundary).ShouldBe(
            (CapacityUnit.Items, 5, "Asia/Tokyo", new TimeOnly(4, 0))
        );
        FocusHours.FromJson(ws.FocusHoursJson)!.Start.ShouldBe(new TimeOnly(8, 0));

        await Should.ThrowAsync<CommandException>(() =>
            _f.Workspaces.SetCapacityAsync(_f.Ws.Id, CapacityUnit.Items, 0)
        );
        await Should.ThrowAsync<TimeZoneNotFoundException>(() =>
            _f.Workspaces.SetTimeAsync(_f.Ws.Id, "Mars/Base", false, TimeOnly.MinValue)
        );
    }
}

public class TodayAllTests : IDisposable
{
    readonly FeatureFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Workspaces_outside_focus_hours_drop_out_and_go_quiet()
    {
        var personal = await _f.Workspaces.CreateAsync(WorkspaceTemplates.Personal);
        await _f.Workspaces.SetFocusHoursAsync(
            _f.Ws.Id,
            FocusHours.Weekdays(new TimeOnly(9, 0), new TimeOnly(18, 0))
        );
        await _f.Workspaces.SetTimeAsync(_f.Ws.Id, "UTC", false, TimeOnly.MinValue);
        await _f.Workspaces.SetTimeAsync(personal.Id, "UTC", false, TimeOnly.MinValue);

        var work = await _f.CreateItemAsync("report", new DateOnly(2026, 10, 5));
        await _f.Bus.SendAsync(
            new CreateItem(
                Guid.CreateVersion7(),
                personal.Id,
                "groceries",
                new DateOnly(2026, 10, 7)
            )
        );
        var lens = new TodayAllService(_f.Uow, _f.Clock, _f.Derive);

        var inHours = await lens.GetAsync(); // Wed 10:00 UTC, within 09–18
        inHours.Sections.Select(s => s.Workspace.Name).ShouldBe(["Work", "Personal"]);
        inHours.TotalNeedsDecision.ShouldBe(1);
        (await lens.BadgeAsync(_f.Ws.Id)).ShouldBe(1);

        _f.Clock.Advance(TimeSpan.FromHours(10)); // 20:00
        var evening = await lens.GetAsync();
        evening.Sections.ShouldHaveSingleItem().Workspace.Id.ShouldBe(personal.Id);
        evening.Quiet.ShouldHaveSingleItem().Id.ShouldBe(_f.Ws.Id);
        (await lens.BadgeAsync(_f.Ws.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Archived_workspaces_are_not_in_the_lens()
    {
        await _f.Workspaces.ArchiveAsync(_f.Ws.Id);
        (
            await new TodayAllService(_f.Uow, _f.Clock, _f.Derive).GetAsync()
        ).Sections.ShouldBeEmpty();
    }
}
