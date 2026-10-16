using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.App.Views;
using Noto.Core.Links;

namespace Noto.App.Tests.Ui;

// The dot is the only place a change is visible at a glance, so it has to actually render: a binding
// that silently resolves to nothing would hide the live-link half of the feature without failing
// anything else. Only a rendered tree can say whether the marker reached the screen (docs/07 §6).
public sealed class LinkChangeDotTests : IDisposable
{
    const string Pr = "https://github.com/acme/app/pull/482";

    readonly AppFixture _app = new();

    public void Dispose() => _app.Dispose();

    static async Task SettleAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static LinkLine Line(bool changed) =>
        LinkLine.From(
            Pr,
            new LinkPreview
            {
                Url = Pr,
                ProviderId = "github",
                Title = "Review PR #482",
                Status = PreviewStatus.Loaded,
                StateHash = "open",
                ViewedStateHash = changed ? "queued" : "open",
            },
            "GitHub"
        );

    // A dot inside a collapsed container is still `IsVisible` on itself — the hidden ancestor is what
    // keeps it off the screen, so the check has to be the effective one.
    static List<Ellipse> VisibleDots(Visual root) =>
        [
            .. root.GetVisualDescendants()
                .OfType<Ellipse>()
                .Where(e => e.Classes.Contains("changed") && e.IsEffectivelyVisible),
        ];

    async Task<ItemRowViewModel> RowAsync(bool changed, bool detail)
    {
        var id = await _app.AddAsync("Review the auth refactor");
        var snap = await _app.Services.Reader.LoadAsync(_app.Workspace.Id);
        var row = ItemRowFactory.Create(await _app.GetAsync(id), snap);
        row.ShowDetail = detail;
        row.Links = [Line(changed)];
        return row;
    }

    static async Task<Window> ShowAsync(ItemRowViewModel row)
    {
        var window = new Window
        {
            Width = 700,
            Height = 140,
            Content = new ItemRowView { DataContext = row },
        };
        window.Show();
        await SettleAsync();
        return window;
    }

    [AvaloniaFact]
    public async Task A_changed_link_puts_a_dot_on_its_chip()
    {
        var window = await ShowAsync(await RowAsync(changed: true, detail: true));

        VisibleDots(window).ShouldHaveSingleItem();
    }

    [AvaloniaFact]
    public async Task A_link_that_has_not_moved_gets_no_dot()
    {
        var window = await ShowAsync(await RowAsync(changed: false, detail: true));

        VisibleDots(window).ShouldBeEmpty();
    }

    // Rows without room drop the chips for one summary line, and the marker has to survive that too.
    [AvaloniaFact]
    public async Task A_compact_row_dots_its_single_link_line_instead()
    {
        var window = await ShowAsync(await RowAsync(changed: true, detail: false));

        VisibleDots(window).ShouldHaveSingleItem();
    }
}
