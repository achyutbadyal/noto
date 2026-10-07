using System.Text;
using System.Text.RegularExpressions;

namespace Noto.Core.Links;

// todo_link semantics (docs/04 §6): lowercase scheme/host, tracking params stripped, no fragment.
public static partial class LinkUrl
{
    public static string? Normalize(string raw)
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme is not ("http" or "https"))
            return null;

        var sb = new StringBuilder();
        sb.Append(uri.Scheme).Append("://").Append(uri.IdnHost.ToLowerInvariant());
        if (!uri.IsDefaultPort)
            sb.Append(':').Append(uri.Port);

        var path = uri.AbsolutePath;
        sb.Append(path.Length > 1 ? path.TrimEnd('/') : path);

        var kept = uri
            .Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !IsTracking(p))
            .ToList();
        if (kept.Count > 0)
            sb.Append('?').Append(string.Join('&', kept));
        return sb.ToString();
    }

    // Distinct normalized URLs in order of appearance.
    public static IReadOnlyList<string> Scan(params string?[] texts)
    {
        var found = new List<string>();
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text))
                continue;
            foreach (Match m in UrlPattern().Matches(text))
                if (Normalize(TrimTrailing(m.Value)) is { } url && !found.Contains(url))
                    found.Add(url);
        }
        return found;
    }

    static bool IsTracking(string param)
    {
        var name = param.Split('=', 2)[0];
        return name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
            || name.Equals("fbclid", StringComparison.OrdinalIgnoreCase)
            || name.Equals("gclid", StringComparison.OrdinalIgnoreCase);
    }

    // Drops sentence punctuation and closers that belong to the surrounding text, not the URL.
    static string TrimTrailing(string url)
    {
        while (url.Length > 0)
        {
            var last = url[^1];
            var strip =
                last is '.' or ',' or ';' or ':' or '!' or '?' or '\'' or '"'
                || (last == ')' && url.Count(c => c == ')') > url.Count(c => c == '('))
                || (last == ']' && url.Count(c => c == ']') > url.Count(c => c == '['));
            if (!strip)
                break;
            url = url[..^1];
        }
        return url;
    }

    [GeneratedRegex(@"https?://[^\s<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
