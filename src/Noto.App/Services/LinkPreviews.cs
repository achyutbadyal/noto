using Noto.Core.Interfaces;
using Noto.Core.Links;
using Noto.Core.Time;
using Noto.Providers;
using Noto.Providers.Preview;

namespace Noto.App.Services;

// What one linked URL is, as the rows and the inspector show it. A preview that has not loaded still gives
// the host, and a failed one says why. Status is empty once the preview has loaded.
public sealed record LinkLine(
    string Url,
    string Title,
    string Provider,
    string? Subtitle,
    IReadOnlyList<string> Facts,
    string? Snippet,
    string? Author,
    string Status
)
{
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
    public bool HasFacts => Facts.Count > 0;
    public bool HasSnippet => !string.IsNullOrEmpty(Snippet);
    public bool HasAuthor => !string.IsNullOrEmpty(Author);
    public bool HasStatus => Status.Length > 0;

    // One line for a row: "PROJ-88 · Login fails on Safari · In Review".
    public string RowText =>
        string.Join(
            " · ",
            new[] { Subtitle, Title, Facts.FirstOrDefault() }.Where(s => !string.IsNullOrEmpty(s))
        );

    public static LinkLine From(string url, LinkPreview? p, string providerName)
    {
        if (p is null)
            return new LinkLine(
                url,
                Host(url),
                providerName,
                null,
                [],
                null,
                null,
                "Not fetched yet"
            );

        return new LinkLine(
            url,
            string.IsNullOrWhiteSpace(p.Title) ? Host(url) : p.Title,
            providerName,
            string.IsNullOrWhiteSpace(p.Subtitle) ? null : p.Subtitle,
            p.ChipFacts.Select(c => c.Text).Where(t => t.Length > 0).ToList(),
            Excerpt(p.Snippet),
            string.IsNullOrWhiteSpace(p.AuthorName) ? null : p.AuthorName,
            StatusText(p.Status, p.ErrorMessage, providerName)
        );
    }

    static string StatusText(PreviewStatus status, string? error, string provider) =>
        status switch
        {
            PreviewStatus.Loaded => "",
            PreviewStatus.Stale => "Showing the last data we had",
            PreviewStatus.AuthRequired => $"Connect {provider} in Settings to see this",
            PreviewStatus.Unavailable => error ?? "No longer accessible",
            PreviewStatus.Error => "Couldn't load this link",
            _ => "Loading",
        };

    static string? Excerpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var clean = string.Join(
            ' ',
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        );
        return clean.Length <= 240 ? clean : clean[..239] + "…";
    }

    static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
}

// Shows what each linked URL is. Reads the preview cache; fetching is a separate step so screens never wait on the network.
public sealed class LinkPreviews(
    IUnitOfWork uow,
    PreviewService previews,
    ProviderRegistry registry,
    IClock clock
)
{
    // Failed previews and ones waiting on a connection are asked again after this, so a list of broken links
    // does not go back to the network on every navigation.
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

    readonly Dictionary<string, string> _names = registry.Providers.ToDictionary(
        p => p.ProviderId,
        p => p.DisplayName
    );

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

    // URLs on these items whose preview is missing or due for a refresh. Fetching skips anything still fresh.
    public async Task<IReadOnlyList<string>> DueAsync(IReadOnlyCollection<Guid> itemIds)
    {
        if (itemIds.Count == 0)
            return [];
        var links = await uow.RunAsync(s => s.Links.ListForItemsAsync(itemIds));
        var urls = links.Select(l => l.Url).Distinct().ToList();
        var cache = await previews.GetCachedAsync(urls);
        var now = clock.UtcNow;
        return urls.Where(u => !cache.TryGetValue(u, out var p) || IsDue(p, now)).ToList();
    }

    public static bool IsDue(LinkPreview p, DateTimeOffset now) =>
        p.Status == PreviewStatus.Loaded
            ? p.ExpiresAt is not { } at || at <= now
            : now - p.FetchedAt >= RetryAfter;

    public Task FetchAsync(IReadOnlyList<string> urls, CancellationToken ct) =>
        previews.RefreshAsync(
            urls.Select(u => new PreviewRequest(u, PreviewPriority.Visible)).ToList(),
            ct
        );

    LinkLine Line(string url, IReadOnlyDictionary<string, LinkPreview> cache)
    {
        cache.TryGetValue(url, out var p);
        var provider = p is null
            ? "Web link"
            : _names.GetValueOrDefault(p.ProviderId, p.ProviderId);
        return LinkLine.From(url, p, provider);
    }
}
