using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noto.App.Logic;
using Noto.App.ViewModels;
using Noto.App.Views;
using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Presets;

namespace Noto.App.Tests.Ui;

// Renders the real window headlessly. Set NOTO_SCREENSHOTS=<dir> to keep the PNGs for a visual check.
public sealed class ScreenshotTests : IDisposable
{
    readonly AppFixture _app = new();

    public void Dispose() => _app.Dispose();

    static string? OutDir => Environment.GetEnvironmentVariable("NOTO_SCREENSHOTS");

    async Task SeedAsync()
    {
        _app.Clock.Advance(TimeSpan.FromDays(-6));
        var deploy = await _app.AddAsync(
            "Deploy v2.3 to staging",
            AppFixture.Today.AddDays(-6),
            60
        );
        _app.Clock.Advance(TimeSpan.FromDays(-1));
        await _app.AddAsync(
            "Review PR #482 for the auth refactor",
            AppFixture.Today.AddDays(-7),
            30
        );
        _app.Clock.Advance(TimeSpan.FromDays(7));
        await _app.AddAsync("Write API tests", AppFixture.Today, 120);
        await _app.AddAsync("1:1 notes for Sam", AppFixture.Today, 15);
        var waiting = await _app.AddAsync("API keys from Priya", AppFixture.Today);
        await _app.Services.Bus.SendAsync(new StartWaiting(waiting, "Priya"));
        var done = await _app.AddAsync("Fix CI flake", AppFixture.Today);
        await _app.Services.Bus.SendAsync(new CompleteItem(done));
        await _app.Services.Bus.SendAsync(new SetPriority(deploy, 2));
        await _app.Services.Workspaces.SetNowAsync(_app.Workspace.Id, deploy);
        await _app.Services.Workspaces.CreateAsync("Personal", "home", BuiltInPresets.Zen, 1);
    }

    (MainWindow Window, ShellViewModel Shell) Open(ThemeVariant? variant = null)
    {
        var shell = new ShellViewModel(_app.Services);
        var window = new MainWindow
        {
            DataContext = shell,
            Width = 1240,
            Height = 800,
        };
        if (variant is not null)
            Avalonia.Application.Current!.RequestedThemeVariant = variant;
        window.Show();
        return (window, shell);
    }

    // Pump the dispatcher so transitions (sidebar width, overlay fade, page fade) finish.
    static async Task SettleAsync()
    {
        for (var i = 0; i < 40; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task SnapAsync(MainWindow window, string name)
    {
        await SettleAsync();
        var frame = window.CaptureRenderedFrame();
        frame.ShouldNotBeNull();
        if (OutDir is { } dir)
        {
            Directory.CreateDirectory(dir);
            frame.Save(Path.Combine(dir, name + ".png"));
        }
    }

    [AvaloniaFact]
    public async Task Today_renders_in_both_themes_with_rows_and_the_inspector()
    {
        await SeedAsync();
        var (window, shell) = Open(ThemeVariant.Dark);
        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Today);
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants().OfType<ItemRowView>().Count().ShouldBeGreaterThanOrEqualTo(4);
        window.GetVisualDescendants().OfType<InspectorView>().ShouldNotBeEmpty();
        await SnapAsync(window, "today-dark");

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        await SnapAsync(window, "today-light");
    }

    [AvaloniaFact]
    public async Task Morning_review_card_renders()
    {
        await SeedAsync();
        var (window, shell) = Open(ThemeVariant.Dark);
        await shell.InitializeAsync();
        shell.Page.ShouldBe(AppPage.Review);
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants().OfType<ReviewView>().ShouldNotBeEmpty();
        await SnapAsync(window, "review-dark");

        await shell.HandleKeyAsync(KeyChord.Of("d"));
        Dispatcher.UIThread.RunJobs();
        await SnapAsync(window, "review-prompt-dark");
    }

    [AvaloniaFact]
    public async Task Command_bar_settings_insights_and_help_render()
    {
        await SeedAsync();
        var (window, shell) = Open(ThemeVariant.Dark);
        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Today);

        await shell.HandleKeyAsync(KeyChord.Of("cmd+k"));
        shell.CommandBar.Text = "dep";
        await shell.CommandBar.UpdateAsync();
        Dispatcher.UIThread.RunJobs();
        await SnapAsync(window, "commandbar-dark");
        shell.CommandBar.Close();

        await shell.GoAsync(AppPage.Settings);
        await SnapAsync(window, "settings-dark");

        await shell.GoAsync(AppPage.Insights);
        await SnapAsync(window, "insights-dark");

        await shell.GoAsync(AppPage.Today);
        shell.IsHelpOpen = true;
        await SnapAsync(window, "help-dark");
    }

    [AvaloniaFact]
    public async Task Layout_views_render()
    {
        await SeedAsync();
        _app.Clock.Advance(TimeSpan.FromDays(-3));
        await _app.Services.Recurrence.CreateRuleAsync(
            _app.Workspace.Id,
            "FREQ=DAILY",
            new RuleTemplate("Morning run"),
            DateOnly.FromDateTime(_app.Clock.UtcNow.UtcDateTime),
            MissedBehavior.Skip
        );
        await _app.Services.Recurrence.CreateRuleAsync(
            _app.Workspace.Id,
            "FREQ=DAILY",
            new RuleTemplate("Read 20 pages"),
            DateOnly.FromDateTime(_app.Clock.UtcNow.UtcDateTime),
            MissedBehavior.Skip
        );
        _app.Clock.Advance(TimeSpan.FromDays(3));
        var due = await _app.AddAsync("Submit report", AppFixture.Today);
        await _app.Services.Bus.SendAsync(new SetDueDate(due, AppFixture.Today.AddDays(-2)));

        var (window, shell) = Open(ThemeVariant.Dark);
        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Today);

        foreach (
            var (preset, name) in new (Preset, string)[]
            {
                (BuiltInPresets.Kanban, "board"),
                (BuiltInPresets.Deadline, "timeline"),
                (BuiltInPresets.Habit, "habits"),
            }
        )
        {
            await _app.Services.Workspaces.UpdateAsync(_app.Workspace.Id, ws => preset.ApplyTo(ws));
            await shell.RefreshAsync();
            if (name == "habits")
                await shell.HandleKeyAsync(KeyChord.Of("x"));
            await SnapAsync(window, name + "-dark");
        }

        await _app.Services.Workspaces.UpdateAsync(
            _app.Workspace.Id,
            ws => BuiltInPresets.Sprint.ApplyTo(ws)
        );
        await shell.RefreshAsync();
        await shell.GoAsync(AppPage.TodayAll);
        await SnapAsync(window, "todayall-dark");
        await shell.GoAsync(AppPage.WeeklyReview);
        await SnapAsync(window, "weekly-dark");
    }

    // A narrow window must adapt (collapse the sidebar, hide the inspector) instead of clipping.
    [AvaloniaFact]
    public async Task Narrow_window_adapts_instead_of_clipping()
    {
        await SeedAsync();
        var (window, shell) = Open(ThemeVariant.Light);
        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Today);

        shell.ViewportWidth = 760;
        Dispatcher.UIThread.RunJobs();

        shell.IsNarrow.ShouldBeTrue();
        shell.IsCompact.ShouldBeTrue();
        shell.ShowToolbarLabels.ShouldBeFalse();
        shell.EffectiveSidebarExpanded.ShouldBeFalse();
        shell.SidebarWidth.ShouldBe(56);
        shell.ShowInspector.ShouldBeFalse();
        await SnapAsync(window, "narrow-light");

        shell.ViewportWidth = 1240;
        Dispatcher.UIThread.RunJobs();
        shell.ShowInspector.ShouldBeTrue();
        shell.EffectiveSidebarExpanded.ShouldBeTrue();
        shell.SidebarWidth.ShouldBe(232);

        // User-collapsed rail at full width: icons must sit centred in the 56px rail.
        shell.ToggleSidebarCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        shell.IsSidebarCollapsed.ShouldBeTrue();
        shell.SidebarWidth.ShouldBe(56);
        await SnapAsync(window, "rail-light");
    }

    // In the icon rail every row must centre its icon on the button, so workspace and nav icons line up.
    [AvaloniaFact]
    public async Task Collapsed_rail_centres_workspace_and_nav_icons()
    {
        await SeedAsync();
        var (window, shell) = Open(ThemeVariant.Light);
        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Today);
        await SettleAsync(); // let the sidebar's item containers realise before measuring

        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
        var workspace = buttons.First(b => b.Classes.Contains("wsTab"));
        var nav = buttons.First(b => b.Classes.Contains("nav") && !b.Classes.Contains("wsTab"));

        // Expanded: the workspace icon sits on the same inset as the nav icons.
        IconCentre(workspace)
            .ShouldBe(
                IconCentre(nav),
                0.5,
                $"expanded icon alignment [ws={IconCentre(workspace):F2} nav={IconCentre(nav):F2}]"
            );

        shell.ToggleSidebarCommand.Execute(null);
        await SettleAsync();

        var workspaceIcon = IconCentre(workspace);
        var navIcon = IconCentre(nav);
        var report =
            $"ws btn={workspace.Bounds.Width:F2} icon={workspaceIcon:F2} | nav btn={nav.Bounds.Width:F2} icon={navIcon:F2}";

        // Collapsed: each icon is centred on its own button...
        workspaceIcon.ShouldBe(
            workspace.Bounds.Width / 2,
            0.5,
            $"workspace icon centre vs button centre [{report}]"
        );
        navIcon.ShouldBe(nav.Bounds.Width / 2, 0.5, $"nav icon centre vs button centre [{report}]");
        // ...and the two rows line up with each other.
        workspace.Bounds.Width.ShouldBe(
            nav.Bounds.Width,
            0.5,
            $"workspace vs nav button width [{report}]"
        );
        workspaceIcon.ShouldBe(navIcon, 0.5, $"workspace vs nav icon centre [{report}]");
    }

    // The in-app guide: topic rail plus the rendered document.
    [AvaloniaFact]
    public async Task Guide_renders_with_its_topic_rail_and_content()
    {
        await SeedAsync();
        var (window, shell) = Open(ThemeVariant.Dark);
        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Today);
        await shell.ShowHelpTopicAsync(HelpTopicIds.Modes);
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants().OfType<HelpView>().ShouldNotBeEmpty();
        shell.Help!.Selected!.Id.ShouldBe(HelpTopicIds.Modes);
        await SnapAsync(window, "guide-dark");

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        await SnapAsync(window, "guide-light");

        // Compact: the topic rail gives way to a horizontal strip rather than squeezing the document.
        shell.ViewportWidth = 760;
        await SnapAsync(window, "guide-compact-light");
    }

    // The document pane must actually scroll. It lives in a bounded Grid row; inside a StackPanel it
    // would be measured with unbounded height and simply overflow, which is what it used to do.
    [AvaloniaFact]
    public async Task Guide_document_pane_scrolls()
    {
        await SeedAsync();
        var (window, shell) = Open(ThemeVariant.Dark);
        await shell.InitializeAsync();
        await shell.ShowHelpTopicAsync(HelpTopicIds.Modes);
        await SettleAsync();

        var doc = window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .First(s => s.Name == "DocScroll");

        doc.Extent.Height.ShouldBeGreaterThan(
            doc.Viewport.Height,
            "the topic is taller than the pane, so it must be scrollable"
        );

        doc.Offset = new Vector(0, 240);
        await SettleAsync();
        doc.Offset.Y.ShouldBeGreaterThan(0, "the pane moved when scrolled");
        await SnapAsync(window, "guide-scrolled-dark");
    }

    // The sidebar must mark the page you are on, and only that page.
    [AvaloniaFact]
    public async Task Sidebar_marks_only_the_current_page()
    {
        await SeedAsync();
        var (window, shell) = Open(ThemeVariant.Light);
        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Today);
        await SettleAsync();

        var guide = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "GuideNav");
        guide.Classes.Contains("selected").ShouldBeFalse("the guide is not the current page");

        await shell.ShowHelpTopicAsync(HelpTopicIds.Start);
        await SettleAsync();
        guide.Classes.Contains("selected").ShouldBeTrue("the guide is the current page");

        await shell.GoAsync(AppPage.Settings);
        await SettleAsync();
        guide.Classes.Contains("selected").ShouldBeFalse("navigating away clears the mark");
    }

    // The capture composer and the detailed create panel.
    [AvaloniaFact]
    public async Task Capture_composer_and_detailed_panel_render()
    {
        await SeedAsync();
        var (window, shell) = Open(ThemeVariant.Dark);
        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Today);
        await SettleAsync();

        var today = (TodayViewModel)shell.Content!;
        today.Add.Text = "Write the launch note";
        today.Add.Estimate = FieldOptions.Durations.First(o => o.Minutes == 90);
        await SnapAsync(window, "capture-dark");

        today.Add.OpenDetailedCommand.Execute(null);
        today.Add.DetailTitle = "Write the launch note";
        today.Add.DetailPriority = FieldOptions.Priorities.First(o => o.Value == 2);
        today.Add.DetailWhen = FieldOptions.Whens.First(o => o.OffsetDays == 1);
        await SettleAsync();

        today.Add.IsDetailedOpen.ShouldBeTrue();
        await SnapAsync(window, "capture-detailed-dark");

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        await SnapAsync(window, "capture-detailed-light");
    }

    // X of the row's icon centre, relative to the button.
    static double IconCentre(Button button)
    {
        var icon = button.GetVisualDescendants().OfType<PathIcon>().First();
        var origin = icon.TranslatePoint(new Point(0, 0), button) ?? new Point();
        return origin.X + icon.Bounds.Width / 2;
    }
}
