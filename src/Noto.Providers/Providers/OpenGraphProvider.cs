using Noto.Core.Links;

namespace Noto.Providers.Providers;

// Fallback for any http(s) URL: title/description/image, no live state.
public sealed class OpenGraphProvider : ProviderBase
{
    public override string ProviderId => ProviderRegistry.FallbackProviderId;
    public override string DisplayName => "Web link";
    public override IReadOnlyList<UrlPattern> UrlPatterns { get; } = [];
    public override IReadOnlyList<AuthMethod> SupportedAuthMethods => [];

    public override AuthConfig GetAuthConfig() => new(null, null, [], "");

    public override bool CanHandle(Uri url, AppConnection? connection) =>
        url.Scheme is "http" or "https";

    public override Uri ApiRoot(string? instanceUrl) => throw new NotSupportedException();

    public override TimeSpan CacheTtl(Uri url) => TimeSpan.FromHours(24);

    public override Task<ConnectionIdentity> ValidateAsync(
        IProviderHttp http,
        CancellationToken ct
    ) => Task.FromResult(new ConnectionIdentity("Web"));

    public override async Task<LinkPreview> FetchAsync(
        Uri url,
        IProviderHttp http,
        CancellationToken ct
    )
    {
        var og = await http.OpenGraphAsync(url, ct) ?? throw new ProviderHttpException(404);
        var p = New(url, og.Title ?? url.IdnHost, null); // no hash inputs: no live state
        p.Snippet = og.Description;
        p.Subtitle = og.SiteName ?? url.IdnHost;
        p.ChipFacts = [new(og.SiteName ?? url.IdnHost)];
        Meta(p, "image", og.Image);
        return p;
    }
}
