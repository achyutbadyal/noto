using Noto.Core.Interfaces;
using Noto.Core.Links;
using Noto.Providers.Preview;

namespace Noto.App.Services;

// One link on a task: its title when a preview has been fetched, the host otherwise, and a short status line.
public sealed record LinkLine(string Url, string Title, string Status);

// Shows what each linked URL is. Reads the preview cache; fetching is a separate step so screens never wait on the network.
public sealed class LinkPreviews(IUnitOfWork uow, PreviewService previews)
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<LinkLine>>> ForItemsAsync(
        IReadOnlyCollection<Guid> itemIds
    )
    {
        if (itemIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<LinkLine>>();
        var links = await uow.RunAsync(s => s.Links.ListForItemsAsync(itemIds));
        var cache = await previews.GetCachedAsync(links.Select(l => l.Url).Distinct().ToList());
        return links
            .GroupBy(l => l.ItemId)
            .ToDictionary(
                g => g.Key,
                g =>
                    (IReadOnlyList<LinkLine>)
                        g.OrderBy(l => l.Position).Select(l => Line(l.Url, cache)).ToList()
            );
    }

    // URLs on these items that have no cached preview yet.
    public async Task<IReadOnlyList<string>> MissingAsync(IReadOnlyCollection<Guid> itemIds)
    {
        if (itemIds.Count == 0)
            return [];
        var links = await uow.RunAsync(s => s.Links.ListForItemsAsync(itemIds));
        var urls = links.Select(l => l.Url).Distinct().ToList();
        var cache = await previews.GetCachedAsync(urls);
        return urls.Where(u => !cache.ContainsKey(u)).ToList();
    }

    public Task FetchAsync(IReadOnlyList<string> urls, CancellationToken ct) =>
        previews.RefreshAsync(
            urls.Select(u => new PreviewRequest(u, PreviewPriority.Visible)).ToList(),
            ct
        );

    static LinkLine Line(string url, IReadOnlyDictionary<string, LinkPreview> cache) =>
        cache.TryGetValue(url, out var p)
            ? new LinkLine(
                url,
                string.IsNullOrWhiteSpace(p.Title) ? Host(url) : p.Title,
                StatusText(p.Status, p.Subtitle)
            )
            : new LinkLine(url, Host(url), "Not fetched yet");

    static string StatusText(PreviewStatus status, string? subtitle) =>
        status switch
        {
            PreviewStatus.Loaded or PreviewStatus.Stale => subtitle ?? "",
            PreviewStatus.AuthRequired => "Connect this app in Settings to see its status",
            PreviewStatus.Unavailable => "Unavailable",
            PreviewStatus.Error => "Couldn't load this link",
            _ => "Loading",
        };

    static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
}
