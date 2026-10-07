using Noto.Core.Links;

namespace Noto.Providers.Providers;

public sealed class FigmaProvider : ProviderBase
{
    public override string ProviderId => "figma";
    public override string DisplayName => "Figma";
    public override IReadOnlyList<AuthMethod> SupportedAuthMethods => [AuthMethod.PersonalToken, AuthMethod.OAuth2];

    public override IReadOnlyList<UrlPattern> UrlPatterns { get; } =
    [
        new("figma.com", "/design/{key}*"), new("www.figma.com", "/design/{key}*"),
        new("figma.com", "/file/{key}*"), new("www.figma.com", "/file/{key}*"),
        new("figma.com", "/board/{key}*"), new("www.figma.com", "/board/{key}*"),
    ];

    public override AuthConfig GetAuthConfig() => new(
        "https://www.figma.com/oauth", "https://api.figma.com/v1/oauth/token", ["file_content:read"],
        "https://help.figma.com/hc/en-us/articles/8085703771159", RequiresClientSecret: true);

    public override Uri ApiRoot(string? instanceUrl) => new("https://api.figma.com/");
    public override TimeSpan CacheTtl(Uri url) => TimeSpan.FromMinutes(30);

    public override IEnumerable<KeyValuePair<string, string>> AuthHeaders(Credential c, AuthMethod method) =>
        [method == AuthMethod.PersonalToken ? new("X-Figma-Token", c.AccessToken) : Bearer(c)];

    public override async Task<ConnectionIdentity> ValidateAsync(IProviderHttp http, CancellationToken ct)
    {
        using var doc = await http.GetJsonAsync(ProviderRequest.Get("v1/me"), ct);
        return new ConnectionIdentity(Str(doc.RootElement, "handle") ?? Str(doc.RootElement, "email") ?? "Figma");
    }

    public override async Task<LinkPreview> FetchAsync(Uri url, IProviderHttp http, CancellationToken ct)
    {
        var key = UrlPatterns.Select(p => p.TryMatch(url, out var c) ? c["key"] : null).FirstOrDefault(k => k is not null);
        if (key is null) return Unavailable(url, "Unsupported Figma URL");

        using var doc = await http.GetJsonAsync(ProviderRequest.Get($"v1/files/{Uri.EscapeDataString(key)}", ("depth", "1")), ct);
        var file = doc.RootElement;
        var modified = Str(file, "lastModified");
        var page = file.TryGetProperty("document", out var d) && d.TryGetProperty("children", out var ch) && ch.GetArrayLength() > 0
            ? Str(ch[0], "name") : null;

        var p = New(url, Str(file, "name") ?? "Figma file", null, modified);
        p.Subtitle = page;
        p.ChipFacts = [new(modified is null ? "Figma" : $"modified {modified[..Math.Min(10, modified.Length)]}")];
        Meta(p, "kind", "file");
        Meta(p, "thumbnail", Str(file, "thumbnailUrl"));
        return p;
    }
}
