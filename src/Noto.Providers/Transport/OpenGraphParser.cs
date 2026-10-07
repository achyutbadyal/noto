using System.Net;
using System.Text.RegularExpressions;

namespace Noto.Providers.Transport;

public static partial class OpenGraphParser
{
    public static OpenGraphData Parse(string html)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in MetaTag().Matches(html))
        {
            var attrs = Attr().Matches(m.Value).ToDictionary(a => a.Groups[1].Value.ToLowerInvariant(), a => WebUtility.HtmlDecode(a.Groups[2].Value + a.Groups[3].Value));
            var key = attrs.GetValueOrDefault("property") ?? attrs.GetValueOrDefault("name");
            if (key is not null && attrs.TryGetValue("content", out var content)) tags.TryAdd(key, content);
        }

        var title = tags.GetValueOrDefault("og:title") ?? tags.GetValueOrDefault("twitter:title")
            ?? (TitleTag().Match(html) is { Success: true } t ? WebUtility.HtmlDecode(t.Groups[1].Value.Trim()) : null);
        return new OpenGraphData(title, tags.GetValueOrDefault("og:description") ?? tags.GetValueOrDefault("description"),
            tags.GetValueOrDefault("og:image"), tags.GetValueOrDefault("og:site_name"));
    }

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex MetaTag();
    [GeneratedRegex("""(\w[\w:-]*)\s*=\s*(?:"([^"]*)"|'([^']*)')""")] private static partial Regex Attr();
    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex TitleTag();
}
