using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Noto.Server.Middleware;

namespace Noto.Server.Gateway;

public sealed record OpenGraphResult(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("image")] string? Image,
    [property: JsonPropertyName("site_name")] string? SiteName
);

// Fetches a public page (SSRF-guarded: public IPs only, 5 redirects, 1 MB, 5 s) and reads its Open Graph tags.
public sealed partial class OpenGraphFetcher(SafeFetcher fetcher)
{
    public async Task<OpenGraphResult> FetchAsync(string? url, CancellationToken ct)
    {
        if (
            !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
        )
            throw ApiException.BadRequest("INVALID_URL", "A public http(s) URL is required");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
        request.Headers.TryAddWithoutValidation("User-Agent", "NotoBot/1.0 (link preview)");
        var response = await fetcher.SendAsync(request, new SafeFetchOptions(), ct);

        var type = response.Headers.GetValueOrDefault("content-type") ?? "";
        if (
            response.Status is < 200 or >= 300
            || !type.Contains("html", StringComparison.OrdinalIgnoreCase)
        )
            return new OpenGraphResult(null, null, null, null);

        return Parse(System.Text.Encoding.UTF8.GetString(response.Body), response.FinalUri);
    }

    public static OpenGraphResult Parse(string html, Uri baseUri)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTag().Matches(html))
        {
            var attrs = Attr()
                .Matches(tag.Value)
                .ToDictionary(
                    m => m.Groups[1].Value.ToLowerInvariant(),
                    m => WebUtility.HtmlDecode(m.Groups[2].Value)
                );
            var key = attrs.GetValueOrDefault("property") ?? attrs.GetValueOrDefault("name");
            if (key is not null && attrs.TryGetValue("content", out var content))
                meta.TryAdd(key, content);
        }

        var title =
            meta.GetValueOrDefault("og:title")
            ?? (
                TitleTag().Match(html) is { Success: true } t
                    ? WebUtility.HtmlDecode(t.Groups[1].Value.Trim())
                    : null
            );
        var description =
            meta.GetValueOrDefault("og:description") ?? meta.GetValueOrDefault("description");
        return new OpenGraphResult(
            Clip(title, 300),
            Clip(description, 1000),
            SafeImage(meta.GetValueOrDefault("og:image"), baseUri),
            Clip(meta.GetValueOrDefault("og:site_name"), 200)
        );
    }

    // Only http(s) image URLs are passed on, never data: or javascript: URIs.
    static string? SafeImage(string? image, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(image))
            return null;
        return Uri.TryCreate(baseUri, image, out var uri) && uri.Scheme is "http" or "https"
            ? uri.ToString()
            : null;
    }

    static string? Clip(string? s, int max) =>
        s is null ? null
        : s.Length <= max ? s
        : s[..max];

    [GeneratedRegex("<meta\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex("([a-zA-Z:_-]+)\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex Attr();

    [GeneratedRegex("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();
}
