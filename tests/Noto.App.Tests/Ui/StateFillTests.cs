using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noto.App.ViewModels;
using Noto.App.Views;

namespace Noto.App.Tests.Ui;

// A state fill — hover, keyboard focus, selection — is a rounded rectangle, and two of them sharing an
// edge stop reading as two items: the focused row, the selected row, and the hovered row under the
// pointer welded into a single shape with a pinched waist. Every list-like surface therefore keeps a
// gap (ThemeBuilder.SurfaceGap). These assert the laid-out geometry rather than the style, so a new
// container that forgets to space its rows fails here.
public sealed class StateFillTests : IDisposable
{
    // The smallest break that still separates two rounded fills. Matches SurfaceGap.
    const double MinGap = 2;

    readonly AppFixture _app = new();

    public void Dispose() => _app.Dispose();

    static async Task SettleAsync()
    {
        for (var i = 0; i < 40; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static void AssertGaps(IReadOnlyList<Visual> items, Visual root, string what)
    {
        var bands = new List<(double Top, double Height)>();
        foreach (var item in items)
            if (item.TranslatePoint(default, root) is { } origin)
                bands.Add((origin.Y, item.Bounds.Height));
        bands = [.. bands.OrderBy(b => b.Top)];

        bands.Count.ShouldBeGreaterThanOrEqualTo(3, $"{what}: too few to check");
        for (var i = 1; i < bands.Count; i++)
        {
            var gap = bands[i].Top - (bands[i - 1].Top + bands[i - 1].Height);
            gap.ShouldBeGreaterThanOrEqualTo(
                MinGap,
                $"{what}: item {i} starts {gap:0.##}px after item {i - 1} — two state fills would touch"
            );
        }
    }

    [AvaloniaFact]
    public async Task List_rows_and_sidebar_items_leave_a_gap_between_neighbours()
    {
        for (var i = 0; i < 6; i++)
            await _app.AddAsync($"Task {i + 1}", AppFixture.Today, 30);

        var shell = new ShellViewModel(_app.Services);
        var window = new MainWindow
        {
            DataContext = shell,
            Width = 1240,
            Height = 800,
        };
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        window.Show();
        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Today);
        await SettleAsync();

        var rows = window
            .GetVisualDescendants()
            .OfType<ItemRowView>()
            .Select(v =>
                v.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("row"))
            )
            .Cast<Visual>()
            .ToList();
        AssertGaps(rows, window, "list rows");

        var navs = window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("nav"))
            .Cast<Visual>()
            .ToList();
        AssertGaps(navs, window, "sidebar items");
    }

    [AvaloniaFact]
    public async Task Dropdown_items_leave_a_gap_between_neighbours()
    {
        var combo = new ComboBox
        {
            ItemsSource = new[] { "one", "two", "three", "four" },
            SelectedIndex = 0,
        };
        var window = new Window
        {
            Width = 240,
            Height = 220,
            Content = combo,
        };
        window.Show();
        combo.IsDropDownOpen = true;
        await SettleAsync();

        var items = Enumerable
            .Range(0, combo.ItemCount)
            .Select(combo.ContainerFromIndex)
            .OfType<Visual>()
            .ToList();
        items.Count.ShouldBeGreaterThanOrEqualTo(3);
        AssertGaps(items, window, "dropdown items");
    }
}
