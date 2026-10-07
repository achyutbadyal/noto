using Noto.App.Logic;
using Noto.App.ViewModels;
using Noto.Core.Models;
using Noto.Core.Presets;

namespace Noto.App.Tests;

public sealed class CommandBarTests : IDisposable
{
    readonly AppFixture _app = new();
    readonly ShellViewModel _shell;

    public CommandBarTests() => _shell = new ShellViewModel(_app.Services);

    public void Dispose() => _app.Dispose();

    CommandBarViewModel Bar => _shell.CommandBar;

    async Task OpenWithAsync(string text)
    {
        await _shell.InitializeAsync();
        await _shell.HandleKeyAsync(KeyChord.Of("cmd+k"));
        Bar.IsOpen.ShouldBeTrue();
        Bar.Text = text;
        await Bar.UpdateAsync();
    }

    [Fact]
    public async Task Empty_query_lists_commands_with_their_shortcuts()
    {
        await OpenWithAsync("");

        Bar.Results.ShouldAllBe(r => r.Kind == ResultKind.Command);
        Bar.Results.First(r => r.Title == "Go to Today").Shortcut.ShouldBe("⌘T");
        Bar.Results.First(r => r.Title == "Undo last action").Shortcut.ShouldBe("⌘Z");
    }

    [Fact]
    public async Task Typing_ranks_matches_fuzzily_and_enter_runs_the_command()
    {
        await OpenWithAsync("back");

        Bar.Results[0].Title.ShouldBe("Go to Backlog");
        await _shell.HandleKeyAsync(KeyChord.Of("Enter"));

        _shell.Page.ShouldBe(AppPage.Backlog);
        Bar.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Arrow_keys_move_the_selection_and_escape_closes()
    {
        await OpenWithAsync("go");
        var count = Bar.Results.Count;
        count.ShouldBeGreaterThan(2);

        await _shell.HandleKeyAsync(KeyChord.Of("ArrowDown"));
        Bar.SelectedIndex.ShouldBe(1);
        await _shell.HandleKeyAsync(KeyChord.Of("ArrowUp"));
        await _shell.HandleKeyAsync(KeyChord.Of("ArrowUp"));
        Bar.SelectedIndex.ShouldBe(0);

        await _shell.HandleKeyAsync(KeyChord.Of("Escape"));
        Bar.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Offers_to_add_what_you_type_and_previews_the_tokens()
    {
        await OpenWithAsync("Write API tests tomorrow ~2h !2");

        var add = Bar.Results.Last();
        add.Kind.ShouldBe(ResultKind.Add);
        add.Title.ShouldBe("Add “Write API tests”");
        add.Detail.ShouldBe("Thu Oct 8 · ~2h · !2");
        Bar.Chips.ShouldBe(["tomorrow", "~2h", "!2"]);

        Bar.SelectedIndex = Bar.Results.Count - 1;
        await Bar.ExecuteSelectedAsync();

        var item = (await _app.Db.RunAsync(s => s.Items.ListAsync(_app.Workspace.Id))).Single();
        (item.Title, item.EstimateMinutes, item.Priority).ShouldBe(("Write API tests", 120, 2));
    }

    [Fact]
    public async Task Finds_items_by_full_text_and_jumps_to_them()
    {
        var id = await _app.AddAsync("Deploy v2.3 to staging", AppFixture.Today);
        await _app.AddAsync("Unrelated", AppFixture.Today);
        await OpenWithAsync("deploy");

        var hit = Bar.Results.First(r => r.Kind == ResultKind.Item);
        hit.Title.ShouldBe("Deploy v2.3 to staging");
        Bar.SelectedIndex = Bar.Results.ToList().IndexOf(hit);
        await Bar.ExecuteSelectedAsync();

        _shell.TodayPage!.FocusedRow!.Id.ShouldBe(id);
    }

    [Fact]
    public async Task Search_hits_in_other_workspaces_switch_to_them()
    {
        var other = await _app.Services.Workspaces.CreateAsync("Home", "home", BuiltInPresets.Zen, 1);
        var id = Guid.CreateVersion7();
        await _app.Services.Bus.SendAsync(new Noto.Core.Commands.CreateItem(id, other.Id, "Buy groceries", AppFixture.Today));
        await OpenWithAsync("groceries");

        await _shell.HandleKeyAsync(KeyChord.Of("Enter"));

        _shell.Selected!.Id.ShouldBe(other.Id);
        _shell.TodayPage!.FocusedRow!.Id.ShouldBe(id);
    }

    [Fact]
    public async Task Backlog_items_open_in_the_backlog()
    {
        var id = await _app.AddAsync("Someday research");
        await OpenWithAsync("research");

        await _shell.HandleKeyAsync(KeyChord.Of("Enter"));

        _shell.Page.ShouldBe(AppPage.Backlog);
        ((BacklogViewModel)_shell.Content!).FocusedRow!.Id.ShouldBe(id);
    }

    [Fact]
    public async Task Preset_and_pressure_commands_change_the_workspace()
    {
        await OpenWithAsync("accountability");
        Bar.Results[0].Title.ShouldBe("Switch preset: Accountability");
        await Bar.ExecuteSelectedAsync();

        var ws = (await _app.Services.Workspaces.GetAsync(_app.Workspace.Id))!;
        ws.Pressure.ShouldBe(Pressure.Relentless);

        await OpenWithAsync("set pressure: gentle");
        await Bar.ExecuteSelectedAsync();
        (await _app.Services.Workspaces.GetAsync(_app.Workspace.Id))!.Pressure.ShouldBe(Pressure.Gentle);
    }

    [Fact]
    public async Task Slash_opens_the_bar_from_a_list_and_workspace_tokens_route_the_add()
    {
        var other = await _app.Services.Workspaces.CreateAsync("Home", "home", BuiltInPresets.Zen, 1);
        await _shell.InitializeAsync();

        await _shell.HandleKeyAsync(KeyChord.Of("/"));
        Bar.IsOpen.ShouldBeTrue();

        Bar.Text = "Buy milk /home";
        await Bar.UpdateAsync();
        Bar.SelectedIndex = Bar.Results.Count - 1;
        await Bar.ExecuteSelectedAsync();

        (await _app.Db.RunAsync(s => s.Items.ListAsync(other.Id))).Single().Title.ShouldBe("Buy milk");
    }

    [Fact]
    public async Task Undo_and_toggles_work_as_commands()
    {
        var id = await _app.AddAsync("A", AppFixture.Today);
        await _shell.InitializeAsync();
        await _shell.HandleKeyAsync(KeyChord.Of("x"));
        await _shell.RefreshAsync();

        await _shell.HandleKeyAsync(KeyChord.Of("cmd+k"));
        Bar.Text = "undo";
        await Bar.UpdateAsync();
        await Bar.ExecuteSelectedAsync();
        (await _app.GetAsync(id)).Status.ShouldBe(ItemStatus.Open);

        await _shell.HandleKeyAsync(KeyChord.Of("cmd+k"));
        Bar.Text = "toggle inspector";
        await Bar.UpdateAsync();
        await Bar.ExecuteSelectedAsync();
        _shell.IsInspectorOpen.ShouldBeFalse();
    }
}
