using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.Core.Links;

namespace Noto.App.Tests;

// The change dot is only worth having if it survives until the user looks at the item and then clears.
// What counts as looking is the item on screen in the inspector — not the page it happens to sit on, and
// not a row that merely scrolled past (docs/07 §6, docs/04 §6).
public sealed class LinkSeenTests : IDisposable
{
    const string Pr = "https://github.com/acme/app/pull/482";
    const string Ticket = "https://acme.atlassian.net/browse/PROJ-88";

    static readonly string PrUrl = LinkUrl.Normalize(Pr)!;
    static readonly string TicketUrl = LinkUrl.Normalize(Ticket)!;

    readonly AppFixture _app = new();
    readonly LinkPreviews _previews;
    readonly InspectorViewModel _inspector;

    public LinkSeenTests()
    {
        _previews = PreviewCache.Build(_app);
        _inspector = _app.Vms.Inspector();
    }

    public void Dispose() => _app.Dispose();

    // The inspector is what "opens" an item, and the decorator reads the focused item off it.
    async Task<WorkspaceSnapshot> OpenAsync(Guid itemId)
    {
        var snap = await _app.Services.Reader.LoadAsync(_app.Workspace.Id);
        await _inspector.LoadAsync(itemId, snap);
        return snap;
    }

    async Task<ItemRowViewModel> RowAsync(Guid itemId, WorkspaceSnapshot snap) =>
        ItemRowFactory.Create(await _app.GetAsync(itemId), snap);

    LinkDecorator Decorator(IReadOnlyList<ItemRowViewModel> rows, bool inspectorShown) =>
        new(_previews, _inspector, () => rows, () => inspectorShown, _ => { });

    async Task<LinkPreview> CachedAsync(string url) =>
        (await _app.Db.RunAsync(s => s.Previews.GetAsync(url)))!;

    [Fact]
    public async Task The_dot_renders_on_the_pass_that_clears_it_and_is_gone_on_the_next()
    {
        var id = await _app.AddAsync($"Review {Pr}");
        await PreviewCache.PutAsync(_app, PrUrl, state: "merged", viewed: "open");
        var snap = await OpenAsync(id);
        var row = await RowAsync(id, snap);

        var decorator = Decorator([row], inspectorShown: true);
        await decorator.DecorateAsync();

        // This pass still carries the dot — the fill happens before the marker is cleared, so the user
        // sees what moved rather than a silent no-op.
        _inspector.Links.Single().HasChange.ShouldBeTrue();
        row.Links.Single().HasChange.ShouldBeTrue();
        (await CachedAsync(PrUrl)).HasChange.ShouldBeFalse();

        // The next pass reads the cleared cache, so the dot is gone.
        await decorator.DecorateAsync();
        _inspector.Links.Single().HasChange.ShouldBeFalse();
        row.Links.Single().HasChange.ShouldBeFalse();
    }

    [Fact]
    public async Task A_row_the_user_never_opened_keeps_its_dot()
    {
        var open = await _app.AddAsync($"Review {Pr}");
        var other = await _app.AddAsync($"Review {Ticket}");
        await PreviewCache.PutAsync(_app, PrUrl, state: "merged", viewed: "open");
        await PreviewCache.PutAsync(_app, TicketUrl, state: "In Review", viewed: "Open");
        var snap = await OpenAsync(open);
        var rows = new[] { await RowAsync(open, snap), await RowAsync(other, snap) };

        var decorator = Decorator(rows, inspectorShown: true);
        await decorator.DecorateAsync();
        await decorator.DecorateAsync();

        rows[0].Links.Single().HasChange.ShouldBeFalse();
        rows[1].Links.Single().HasChange.ShouldBeTrue();
        (await CachedAsync(TicketUrl)).HasChange.ShouldBeTrue();
    }

    [Fact]
    public async Task Nothing_is_cleared_while_the_inspector_is_hidden()
    {
        var id = await _app.AddAsync($"Review {Pr}");
        await PreviewCache.PutAsync(_app, PrUrl, state: "merged", viewed: "open");
        var snap = await OpenAsync(id);
        var row = await RowAsync(id, snap);

        // A narrow window shows no card, so the user has looked at nothing.
        var decorator = Decorator([row], inspectorShown: false);
        await decorator.DecorateAsync();
        await decorator.DecorateAsync();

        (await CachedAsync(PrUrl)).HasChange.ShouldBeTrue();
        row.Links.Single().HasChange.ShouldBeTrue();
    }

    [Fact]
    public async Task A_preview_that_has_not_moved_is_left_alone()
    {
        var id = await _app.AddAsync($"Review {Pr}");
        await PreviewCache.PutAsync(_app, PrUrl, state: "open", viewed: "open");
        var snap = await OpenAsync(id);
        var row = await RowAsync(id, snap);

        await Decorator([row], inspectorShown: true).DecorateAsync();

        row.Links.Single().HasChange.ShouldBeFalse();
        (await CachedAsync(PrUrl)).UserLastViewedAt.ShouldBeNull();
    }
}
