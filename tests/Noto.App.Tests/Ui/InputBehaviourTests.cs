using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noto.App.ViewModels;
using Noto.App.Views;

namespace Noto.App.Tests.Ui;

// Rows, panels and blank space are not focusable, so without an explicit handler a click outside a text
// field left the caret blinking in it. This pins the window-level behaviour.
public sealed class InputBehaviourTests
{
    [AvaloniaFact]
    public async Task Clicking_outside_a_text_field_takes_focus_out_of_it()
    {
        var app = new AppFixture();
        try
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
            for (var i = 0; i < 30; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }

            var box = window
                .GetVisualDescendants()
                .OfType<TextBox>()
                .First(t => t.Name == "AddBox");
            box.Focus();
            Dispatcher.UIThread.RunJobs();
            box.IsFocused.ShouldBeTrue("the field should be focusable to begin with");

            // Click the middle of the (non-focusable) list area.
            var list = window.GetVisualDescendants().OfType<SectionsList>().First();
            var point =
                list.TranslatePoint(
                    new Point(list.Bounds.Width / 2, list.Bounds.Height / 2),
                    window
                ) ?? new Point();
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            box.IsFocused.ShouldBeFalse("clicking outside must leave the field");
        }
        finally
        {
            app.Dispose();
        }
    }

    // The command bar's overlay fades instead of toggling IsVisible, so focus has to be moved by hand.
    // Without it, Cmd-K left the caret in whatever field was last used and typing went there instead.
    [AvaloniaFact]
    public async Task Opening_the_command_bar_focuses_its_field_and_closing_gives_focus_back()
    {
        var app = new AppFixture();
        try
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
            await SettleAsync();

            var addBox = window
                .GetVisualDescendants()
                .OfType<TextBox>()
                .First(t => t.Name == "AddBox");
            addBox.Focus();
            Dispatcher.UIThread.RunJobs();
            addBox.IsFocused.ShouldBeTrue("the field should start focused");

            await shell.CommandBar.OpenAsync();
            await SettleAsync();

            var bar = window
                .GetVisualDescendants()
                .OfType<TextBox>()
                .First(t => t.Name == "BarBox");
            bar.IsFocused.ShouldBeTrue("opening the command bar takes focus");
            addBox.IsFocused.ShouldBeFalse();

            shell.CommandBar.Close();
            await SettleAsync();
            bar.IsFocused.ShouldBeFalse(
                "closing hands focus back so single-key shortcuts work again"
            );
        }
        finally
        {
            app.Dispose();
        }
    }

    static async Task SettleAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }
}
