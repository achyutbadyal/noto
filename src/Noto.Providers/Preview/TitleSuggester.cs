using Noto.Core.Links;

namespace Noto.Providers.Preview;

// Title for a todo created by pasting a bare URL into an empty add field.
public static class TitleSuggester
{
    public static string ForPaste(LinkPreview p)
    {
        var kind = p.Metadata.TryGetValue("kind", out var k) ? k.GetString() : null;
        var number = p.Metadata.TryGetValue("number", out var n) ? n.GetString() : null;
        return (kind, number) switch
        {
            ("pr", { } num) => $"Review: {p.Title} (#{num})",
            ("issue", { } num) when p.ProviderId is "jira" or "linear" => $"{num}: {p.Title}",
            ("issue", { } num) => $"{p.Title} (#{num})",
            _ => p.Title,
        };
    }
}
