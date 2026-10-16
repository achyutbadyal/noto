using Noto.App.Services;

namespace Noto.App.ViewModels;

// Puts link summaries on the rows on screen and on the inspector's item. Previews that are missing or expired
// are fetched in the background and then shown; a failure only leaves the links as plain titles.
public sealed class LinkDecorator(
    LinkPreviews? previews,
    InspectorViewModel inspector,
    Func<IReadOnlyList<ItemRowViewModel>> visibleRows,
    Func<bool> inspectorShown,
    Action<string> report
)
{
    public async Task DecorateAsync()
    {
        if (previews is null)
            return;
        var rows = visibleRows();
        var focused = inspector.Item?.Id;
        try
        {
            await ApplyAsync(previews, rows, focused);
            var due = await previews.DueAsync(IdsOf(rows, focused));
            if (due.Count > 0)
                _ = FetchAsync(previews, due);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Enrichment is optional: navigation must not fail because a link could not be read.
            report($"Links unavailable: {e.Message}");
        }
    }

    static List<Guid> IdsOf(IReadOnlyList<ItemRowViewModel> rows, Guid? focused) =>
        rows.Select(r => r.Id).Concat(focused is { } f ? [f] : []).Distinct().ToList();

    async Task ApplyAsync(
        LinkPreviews previews,
        IReadOnlyList<ItemRowViewModel> rows,
        Guid? focused
    )
    {
        var summaries = await previews.ForItemsAsync(IdsOf(rows, focused));
        foreach (var row in rows)
            row.Links = summaries.GetValueOrDefault(row.Id, []);
        var shown = focused is { } id ? summaries.GetValueOrDefault(id, []) : [];
        inspector.Links = shown;

        // Clearing the dots happens after the read that produced the fill, and only for the item on
        // screen. After, because the fill's data was read before the write — so this pass still carries
        // the dot and the next read is what drops it. Only that item, because a dot means "moved since
        // you last opened it": a row the user never opened keeps its dot, and a hidden inspector has
        // looked at nothing (docs/07 §6).
        if (inspectorShown())
            await previews.MarkSeenAsync(shown);
    }

    async Task FetchAsync(LinkPreviews previews, IReadOnlyList<string> urls)
    {
        try
        {
            await previews.FetchAsync(urls, CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            report($"Couldn't fetch link previews: {e.Message}");
            return;
        }
        // The screen may have changed while the fetch ran, so read what is visible now.
        await ApplyAsync(previews, visibleRows(), inspector.Item?.Id);
    }
}
