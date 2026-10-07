using Noto.Core.Links;

namespace Noto.Providers.Providers;

public sealed class ConfluenceProvider : ProviderBase
{
    public override string ProviderId => "confluence";
    public override string DisplayName => "Confluence";
    public override bool IsInstanceBased => true;

    // Atlassian API token (email + token). OAuth 3LO needs cloud-id routing and is not implemented.
    public override IReadOnlyList<AuthMethod> SupportedAuthMethods => [AuthMethod.ApiKey];
    public override IReadOnlyList<UrlPattern> UrlPatterns { get; } =
    [new("*.atlassian.net", "/wiki/spaces/{space}/pages/{id}*")];

    public override AuthConfig GetAuthConfig() =>
        new(null, null, [], "https://id.atlassian.com/manage-profile/security/api-tokens");

    public override Uri ApiRoot(string? instanceUrl) => new($"{instanceUrl!.TrimEnd('/')}/");

    public override TimeSpan CacheTtl(Uri url) => TimeSpan.FromMinutes(30);

    public override IEnumerable<KeyValuePair<string, string>> AuthHeaders(
        Credential c,
        AuthMethod method
    ) => [Basic(c)];

    public override bool LooksLikeOwn(Uri url) =>
        url.AbsolutePath.StartsWith("/wiki/spaces/") && url.AbsolutePath.Contains("/pages/");

    public override async Task<ConnectionIdentity> ValidateAsync(
        IProviderHttp http,
        CancellationToken ct
    )
    {
        using var doc = await http.GetJsonAsync(ProviderRequest.Get("rest/api/3/myself"), ct);
        return new ConnectionIdentity(Str(doc.RootElement, "displayName") ?? "Confluence");
    }

    public override async Task<LinkPreview> FetchAsync(
        Uri url,
        IProviderHttp http,
        CancellationToken ct
    )
    {
        if (!UrlPatterns[0].TryMatch(url, out var c))
            return Unavailable(url, "Unsupported Confluence URL");

        using var doc = await http.GetJsonAsync(
            ProviderRequest.Get($"wiki/api/v2/pages/{Uri.EscapeDataString(c["id"])}"),
            ct
        );
        var page = doc.RootElement;
        var version = Str(page, "version", "number");
        var edited = Str(page, "version", "createdAt");

        var p = New(url, Str(page, "title") ?? "", null, version);
        p.Subtitle = c["space"];
        p.ChipFacts =
        [
            new(c["space"]),
            new(edited is null ? "page" : $"edited {edited[..Math.Min(10, edited.Length)]}"),
        ];
        Meta(p, "kind", "page");
        Meta(p, "version", version);
        return p;
    }
}
