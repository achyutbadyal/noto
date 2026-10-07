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
        var deploy = await _app.AddAsync("Deploy v2.3 to staging", AppFixture.Today.AddDays(-6), 60);
        _app.Clock.Advance(TimeSpan.FromDays(-1));
        await _app.AddAsync("Review PR #482 for the auth refactor", AppFixture.Today.AddDays(-7), 30);
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
        var window = new MainWindow { DataContext = shell, Width = 1240, Height = 800 };
        if (variant is not null) Avalonia.Application.Current!.RequestedThemeVariant = variant;
        window.Show();
        return (window, shell);
    }

    static void Snap(MainWindow window, string name)
    {
        Dispatcher.UIThread.RunJobs();
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
        Snap(window, "today-dark");

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        Snap(window, "today-light");
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
        Snap(window, "review-dark");

        await shell.HandleKeyAsync(KeyChord.Of("d"));
        Dispatcher.UIThread.RunJobs();
        Snap(window, "review-prompt-dark");
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
        Snap(window, "commandbar-dark");
        shell.CommandBar.Close();

        await shell.GoAsync(AppPage.Settings);
        Snap(window, "settings-dark");

        await shell.GoAsync(AppPage.Insights);
        Snap(window, "insights-dark");

        await shell.GoAsync(AppPage.Today);
        shell.IsHelpOpen = true;
        Snap(window, "help-dark");
    }

    [AvaloniaFact]
    public async Task Layout_views_render()
    {
        await SeedAsync();
        _app.Clock.Advance(TimeSpan.FromDays(-3));
        await _app.Services.Recurrence.CreateRuleAsync(_app.Workspace.Id, "FREQ=DAILY", new RuleTemplate("Morning run"),
            DateOnly.FromDateTime(_app.Clock.UtcNow.UtcDateTime), MissedBehavior.Skip);
        await _app.Services.Recurrence.CreateRuleAsync(_app.Workspace.Id, "FREQ=DAILY", new RuleTemplate("Read 20 pages"),
            DateOnly.FromDateTime(_app.Clock.UtcNow.UtcDateTime), MissedBehavior.Skip);
        _app.Clock.Advance(TimeSpan.FromDays(3));
        var due = await _app.AddAsync("Submit report", AppFixture.Today);
        await _app.Services.Bus.SendAsync(new SetDueDate(due, AppFixture.Today.AddDays(-2)));

        var (window, shell) = Open(ThemeVariant.Dark);
        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Today);

        foreach (var (preset, name) in new (Preset, string)[]
                 { (BuiltInPresets.Kanban, "board"), (BuiltInPresets.Deadline, "timeline"), (BuiltInPresets.Habit, "habits") })
        {
            await _app.Services.Workspaces.UpdateAsync(_app.Workspace.Id, ws => preset.ApplyTo(ws));
            await shell.RefreshAsync();
            if (name == "habits") await shell.HandleKeyAsync(KeyChord.Of("x"));
            Snap(window, name + "-dark");
        }

        await _app.Services.Workspaces.UpdateAsync(_app.Workspace.Id, ws => BuiltInPresets.Sprint.ApplyTo(ws));
        await shell.RefreshAsync();
        await shell.GoAsync(AppPage.TodayAll);
        Snap(window, "todayall-dark");
        await shell.GoAsync(AppPage.WeeklyReview);
        Snap(window, "weekly-dark");
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
        Snap(window, "narrow-light");

        shell.ViewportWidth = 1240;
        Dispatcher.UIThread.RunJobs();
        shell.ShowInspector.ShouldBeTrue();
        shell.EffectiveSidebarExpanded.ShouldBeTrue();
        shell.SidebarWidth.ShouldBe(232);
    }
}
