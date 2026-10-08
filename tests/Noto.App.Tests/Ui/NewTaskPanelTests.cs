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

// Picking an option in a dropdown inside "More options" must select it, not dismiss the panel.
public sealed class NewTaskPanelTests
{
    [AvaloniaFact]
    public async Task Choosing_a_dropdown_option_in_the_new_task_panel_keeps_the_panel_open()
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
            Pump();

            var today = shell.TodayPage!;
            today.Add.OpenDetailedCommand.Execute(null);
            Pump();
            today.Add.IsDetailedOpen.ShouldBeTrue();

            var priority = window
                .GetVisualDescendants()
                .OfType<ComboBox>()
                .First(c => ReferenceEquals(c.ItemsSource, today.Add.Priorities));
            priority.IsDropDownOpen = true;
            Pump();

            var option = (ComboBoxItem)priority.ContainerFromIndex(2)!;
            var centre =
                option.TranslatePoint(
                    new Point(option.Bounds.Width / 2, option.Bounds.Height / 2),
                    window
                ) ?? throw new InvalidOperationException("the option is not on screen");
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            Pump();

            today.Add.IsDetailedOpen.ShouldBeTrue("picking an option must not close the panel");
            today.Add.DetailPriority.ShouldBe(today.Add.Priorities[2]);
        }
        finally
        {
            app.Dispose();
        }
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
