using Noto.Providers.Auth;

namespace Noto.Providers.Providers;

// Atlassian Cloud, shared by Jira and Confluence. The Noto server runs the OAuth grant (its secret, its audience
// parameter). Once the app holds a token, one grant covers every site the user can reach: the site is resolved
// to its cloud ID here, and requests go through api.atlassian.com/ex/{product}/{id}.
public static class AtlassianCloud
{
    const string ResourcesUrl = "https://api.atlassian.com/oauth/token/accessible-resources";
    const string PatHelpUrl = "https://id.atlassian.com/manage-profile/security/api-tokens";

    public static AuthConfig Config() =>
        new(null, null, [], PatHelpUrl, ServerProviderId: ServerProviderId);

    // One Atlassian app on the server serves Jira and Confluence.
    public const string ServerProviderId = "atlassian";

    // `product` is the gateway segment: "jira" or "confluence". A blank site picks the account's first site.
    public static async Task<OAuthSite> ResolveAsync(
        IProviderHttp http,
        string product,
        string? site,
        CancellationToken ct
    )
    {
        using var doc = await http.GetJsonAsync(ProviderRequest.Get(ResourcesUrl), ct);
        var resources = doc
            .RootElement.EnumerateArray()
            .Select(r =>
                (Id: r.GetProperty("id").GetString()!, Url: r.GetProperty("url").GetString()!)
            )
            .ToList();

        var wanted = string.IsNullOrWhiteSpace(site) ? null : Normalize(site);
        var match = wanted is null
            ? resources.FirstOrDefault()
            : resources.FirstOrDefault(r => Normalize(r.Url) == wanted);
        if (match.Url is null)
            throw new AuthRequiredException(
                wanted is null
                    ? "This Atlassian account has no sites Noto can use."
                    : $"{site!.Trim()} is not a site on this Atlassian account."
            );

        return new OAuthSite(match.Url, $"https://api.atlassian.com/ex/{product}/{match.Id}/");
    }

    // "acme.atlassian.net" and "https://acme.atlassian.net/" name the same site.
    static string Normalize(string text)
    {
        var t = text.Trim();
        if (!t.Contains("://", StringComparison.Ordinal))
            t = "https://" + t;
        return Uri.TryCreate(t, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant()
            : t.ToLowerInvariant();
    }
}
