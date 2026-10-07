using System.Text.Json;
using System.Text.RegularExpressions;
using Noto.Core.Links;

namespace Noto.Providers.Providers;

public sealed partial class NotionProvider : ProviderBase
{
    public const string ShareHint = "Share this page with Noto in Notion to see live status";
    static readonly IReadOnlyDictionary<string, string> Version = new Dictionary<string, string> { ["Notion-Version"] = "2022-06-28" };

    public override string ProviderId => "notion";
    public override string DisplayName => "Notion";
    public override IReadOnlyList<AuthMethod> SupportedAuthMethods => [AuthMethod.OAuth2, AuthMethod.PersonalToken];

    public override IReadOnlyList<UrlPattern> UrlPatterns { get; } =
    [
        new("notion.so", "/*"), new("www.notion.so", "/*"), new("*.notion.site", "/*"),
    ];

    // Notion's token endpoint takes client credentials via HTTP basic; the generic form flow is unverified against it.
    public override AuthConfig GetAuthConfig() => new(
        "https://api.notion.com/v1/oauth/authorize", "https://api.notion.com/v1/oauth/token", [], "https://www.notion.so/profile/integrations",
        UsesPkce: false, RequiresClientSecret: true);

    public override Uri ApiRoot(string? instanceUrl) => new("https://api.notion.com/");
    public override TimeSpan CacheTtl(Uri url) => TimeSpan.FromMinutes(30);

    [GeneratedRegex(@"([0-9a-f]{32})$", RegexOptions.IgnoreCase)] private static partial Regex Id32();

    public override async Task<ConnectionIdentity> ValidateAsync(IProviderHttp http, CancellationToken ct)
    {
        using var doc = await http.GetJsonAsync(new ProviderRequest("GET", "v1/users/me", Headers: Version), ct);
        return new ConnectionIdentity(Str(doc.RootElement, "bot", "workspace_name") ?? Str(doc.RootElement, "name") ?? "Notion");
    }

    public override async Task<LinkPreview> FetchAsync(Uri url, IProviderHttp http, CancellationToken ct)
    {
        var last = url.AbsolutePath.Trim('/').Split('/').Last().Replace("-", "");
        if (Id32().Match(last) is not { Success: true } m) return Unavailable(url, "Unsupported Notion URL");
        var id = m.Groups[1].Value;

        JsonDocument doc;
        try { doc = await http.GetJsonAsync(new ProviderRequest("GET", $"v1/pages/{id}", Headers: Version), ct); }
        catch (ProviderHttpException e) when (e.Status is 403 or 404)
        {
            // Integrations only see pages shared with them.
            var hidden = Unavailable(url, ShareHint);
            Meta(hidden, "hint", "share_with_integration");
            return hidden;
        }

        using (doc)
        {
            var page = doc.RootElement;
            var edited = Str(page, "last_edited_time");
            var title = page.TryGetProperty("properties", out var props)
                ? props.EnumerateObject().Where(x => Str(x.Value, "type") == "title")
                    .Select(x => string.Concat(x.Value.GetProperty("title").EnumerateArray().Select(t => Str(t, "plain_text")))).FirstOrDefault()
                : null;

            var p = New(url, string.IsNullOrWhiteSpace(title) ? "Untitled" : title, null, edited);
            p.ChipFacts = [new(edited is null ? "Notion" : $"edited {edited[..Math.Min(10, edited.Length)]}")];
            p.Subtitle = "Notion";
            Meta(p, "kind", "page");
            Meta(p, "last_edited_by", Str(page, "last_edited_by", "id"));
            return p;
        }
    }
}
