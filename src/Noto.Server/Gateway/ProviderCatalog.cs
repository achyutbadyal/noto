namespace Noto.Server.Gateway;

public sealed record OAuthDef(
    string AuthorizeUrl,
    string TokenUrl,
    string[] Scopes,
    bool Pkce = true,
    bool JsonTokenRequest = false,
    bool BasicAuthToken = false,
    IReadOnlyDictionary<string, string>? ExtraAuthorizeParams = null
);

// Hosts may be exact ("api.github.com") or one-label wildcards ("*.atlassian.net").
public sealed record ProviderDef(
    string Id,
    string Name,
    string[] Hosts,
    OAuthDef? OAuth,
    bool AllowInstanceUrl = false,
    string? PathPrefix = null,
    string? GraphQlHost = null,
    string? GraphQlPath = null
)
{
    public string EnvPrefix => Id.ToUpperInvariant();
}

public static class ProviderCatalog
{
    static readonly ProviderDef[] Providers =
    [
        new(
            "github",
            "GitHub",
            ["api.github.com"],
            new OAuthDef(
                "https://github.com/login/oauth/authorize",
                "https://github.com/login/oauth/access_token",
                ["repo", "read:user"],
                Pkce: true
            ),
            GraphQlHost: "api.github.com",
            GraphQlPath: "/graphql"
        ),
        new(
            "slack",
            "Slack",
            ["slack.com"],
            new OAuthDef(
                "https://slack.com/oauth/v2/authorize",
                "https://slack.com/api/oauth.v2.access",
                [
                    "channels:history",
                    "groups:history",
                    "im:history",
                    "mpim:history",
                    "channels:read",
                    "groups:read",
                ],
                Pkce: false
            ),
            PathPrefix: "/api/"
        ),
        new(
            "atlassian",
            "Atlassian",
            ["api.atlassian.com", "*.atlassian.net"],
            new OAuthDef(
                "https://auth.atlassian.com/authorize",
                "https://auth.atlassian.com/oauth/token",
                ["read:jira-work", "read:confluence-content.all", "offline_access"],
                Pkce: false,
                JsonTokenRequest: true,
                ExtraAuthorizeParams: new Dictionary<string, string>
                {
                    ["audience"] = "api.atlassian.com",
                    ["prompt"] = "consent",
                }
            ),
            AllowInstanceUrl: true
        ),
        new(
            "linear",
            "Linear",
            ["api.linear.app"],
            new OAuthDef(
                "https://linear.app/oauth/authorize",
                "https://api.linear.app/oauth/token",
                ["read"],
                Pkce: true
            ),
            GraphQlHost: "api.linear.app",
            GraphQlPath: "/graphql"
        ),
        new(
            "gitlab",
            "GitLab",
            ["gitlab.com"],
            new OAuthDef(
                "https://gitlab.com/oauth/authorize",
                "https://gitlab.com/oauth/token",
                ["read_api"],
                Pkce: true
            ),
            AllowInstanceUrl: true
        ),
        new(
            "notion",
            "Notion",
            ["api.notion.com"],
            new OAuthDef(
                "https://api.notion.com/v1/oauth/authorize",
                "https://api.notion.com/v1/oauth/token",
                [],
                Pkce: false,
                JsonTokenRequest: true,
                BasicAuthToken: true,
                ExtraAuthorizeParams: new Dictionary<string, string> { ["owner"] = "user" }
            )
        ),
        new(
            "figma",
            "Figma",
            ["api.figma.com"],
            new OAuthDef(
                "https://www.figma.com/oauth",
                "https://api.figma.com/v1/oauth/token",
                ["files:read"],
                Pkce: false
            )
        ),
    ];

    public static ProviderDef? Find(string id) => Providers.FirstOrDefault(p => p.Id == id);

    public static IReadOnlyList<ProviderDef> All => Providers;
}

public static class HostPattern
{
    public static bool Matches(string pattern, string host)
    {
        if (!pattern.StartsWith("*."))
            return string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
        var suffix = pattern[1..]; // ".atlassian.net"
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;
        var label = host[..^suffix.Length];
        return label.Length > 0 && !label.Contains('.');
    }
}
