using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.App.Views;
using Noto.Platform.Abstractions;
using Noto.Providers.Ai;

namespace Noto.App.Tests.Ui;

// The sparkle is a real control in the new-task panel, and it is only on screen when a provider is
// configured and switched on (docs/07 §19 — AI only when asked).
public sealed class AiSparkleTests
{
    const string SparkleName = "Fill fields from the title with AI";

    [AvaloniaFact]
    public async Task No_sparkle_is_shown_when_ai_is_off()
    {
        var app = new AppFixture();
        try
        {
            var (window, shell) = await OpenTodayAsync(app);
            shell.TodayPage!.Add.OpenDetailedCommand.Execute(null);
            Pump();

            VisibleSparkles(window).ShouldBeEmpty();
        }
        finally
        {
            app.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task A_sparkle_is_shown_when_ai_is_on()
    {
        var app = new AppFixture(ai: EnabledAi());
        try
        {
            var (window, shell) = await OpenTodayAsync(app);
            shell.TodayPage!.Add.OpenDetailedCommand.Execute(null);
            Pump();

            VisibleSparkles(window).Count().ShouldBe(1);
        }
        finally
        {
            app.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task Settings_offers_an_ai_pane_with_a_global_off_switch()
    {
        var app = new AppFixture(ai: EnabledAi());
        try
        {
            var (_, shell) = await OpenTodayAsync(app);
            await shell.GoAsync(AppPage.Settings);
            Pump();

            shell.Settings!.HasAi.ShouldBeTrue();
            shell.Settings.Categories.Select(c => c.Title).ShouldContain("AI suggestions");

            // "Off" is offered as a mode, which is the hide-everything switch.
            shell.Settings.AiModes.Select(m => m.Mode).ShouldContain(Noto.Providers.Ai.AiMode.Off);
        }
        finally
        {
            app.Dispose();
        }
    }

    static AiOptions EnabledAi()
    {
        var ai = new AiOptions(new InMemoryUiState(), new InMemoryKeyring(), new HttpClient());
        // A local server needs no key, so the mode is usable without touching the keyring.
        ai.SaveAsync(AiMode.Local, "http://localhost:11434/v1", "llama3", null)
            .GetAwaiter()
            .GetResult();
        return ai;
    }

    static IEnumerable<Button> VisibleSparkles(MainWindow window) =>
        window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(b => AutomationProperties.GetName(b) == SparkleName && b.IsVisible);

    static async Task<(MainWindow Window, ShellViewModel Shell)> OpenTodayAsync(AppFixture app)
    {
        var shell = new ShellViewModel(app.Services);
        var window = new MainWindow
        {
            DataContext = shell,
            Width = 1240,
            Height = 800,
        };
        window.Show();
        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Today);
        Pump();
        return (window, shell);
    }

    static void Pump()
    {
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }
}
