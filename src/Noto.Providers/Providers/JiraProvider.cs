using System.Text.Json;
using System.Text.RegularExpressions;
using Noto.Core.Links;

namespace Noto.Providers.Providers;

public sealed partial class JiraProvider : ProviderBase
{
    const string Fields =
        "summary,status,assignee,priority,labels,customfield_10016,customfield_10020";

    public override string ProviderId => "jira";
    public override string DisplayName => "Jira";
    public override bool IsInstanceBased => true;
    public override bool RequiresInstanceUrl => true;

    public override bool UsesUsername(AuthMethod method) => method == AuthMethod.ApiKey;

    // Cloud: OAuth 3LO (browser) or API token (email + token, basic). Data Center: PAT (bearer).
    public override IReadOnlyList<AuthMethod> SupportedAuthMethods =>
        [AuthMethod.OAuth2, AuthMethod.ApiKey, AuthMethod.PersonalToken];

    public override IReadOnlyList<UrlPattern> UrlPatterns { get; } =
    [new("*.atlassian.net", "/browse/{key}")]; // self-hosted hosts match via their connection

    public override AuthConfig GetAuthConfig() => AtlassianCloud.Config();

    public override bool OAuthAcceptsSite => true;

    public override async Task<OAuthSite?> ResolveOAuthSiteAsync(
        IProviderHttp http,
        string? instanceUrl,
        CancellationToken ct
    ) => await AtlassianCloud.ResolveAsync(http, "jira", instanceUrl, ct);

    public override Uri ApiRoot(string? instanceUrl) => new($"{instanceUrl!.TrimEnd('/')}/");

    public override TimeSpan CacheTtl(Uri url) => TimeSpan.FromMinutes(10);

    public override IEnumerable<KeyValuePair<string, string>> AuthHeaders(
        Credential c,
        AuthMethod method
    ) => [method == AuthMethod.ApiKey ? Basic(c) : Bearer(c)];

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]+-\d+$")]
    private static partial Regex IssueKey();

    public override bool LooksLikeOwn(Uri url) => KeyOf(url) is not null;

    static string? KeyOf(Uri url)
    {
        var parts = url.AbsolutePath.Trim('/').Split('/');
        return parts.Length >= 2 && parts[0] == "browse" && IssueKey().IsMatch(parts[1])
            ? parts[1]
            : null;
    }

    public override async Task<ConnectionIdentity> ValidateAsync(
        IProviderHttp http,
        CancellationToken ct
    )
    {
        try
        {
            using var doc = await http.GetJsonAsync(ProviderRequest.Get("rest/api/3/myself"), ct);
            return new ConnectionIdentity(Str(doc.RootElement, "displayName") ?? "Jira");
        }
        catch (ProviderHttpException e) when (e.Status == 404)
        {
            using var doc = await http.GetJsonAsync(ProviderRequest.Get("rest/api/2/myself"), ct);
            return new ConnectionIdentity(Str(doc.RootElement, "displayName") ?? "Jira");
        }
    }

    public override async Task<LinkPreview> FetchAsync(
        Uri url,
        IProviderHttp http,
        CancellationToken ct
    ) => (await FetchBatchAsync([url], http, ct))[0];

    // JQL `key in (…)` batching: one search per 50 keys.
    public override async Task<IReadOnlyList<LinkPreview>> FetchBatchAsync(
        IReadOnlyList<Uri> urls,
        IProviderHttp http,
        CancellationToken ct
    )
    {
        var results = new LinkPreview[urls.Count];
        var keys = urls.Select(KeyOf).ToList();
        for (var i = 0; i < urls.Count; i++)
            if (keys[i] is null)
                results[i] = Unavailable(urls[i], "Unsupported Jira URL");

        var cloud = urls.Any(u =>
            u.IdnHost.EndsWith(".atlassian.net", StringComparison.OrdinalIgnoreCase)
        );
        foreach (
            var chunk in Enumerable.Range(0, urls.Count).Where(i => keys[i] is not null).Chunk(50)
        )
        {
            var jql = $"key in ({string.Join(",", chunk.Select(i => keys[i]).Distinct())})";
            var request = cloud
                ? ProviderRequest.Get(
                    "rest/api/3/search/jql",
                    ("jql", jql),
                    ("fields", Fields),
                    ("maxResults", "50")
                )
                : ProviderRequest.Get(
                    "rest/api/2/search",
                    ("jql", jql),
                    ("fields", Fields),
                    ("maxResults", "50")
                );
            using var doc = await http.GetJsonAsync(request, ct);

            var byKey = doc
                .RootElement.GetProperty("issues")
                .EnumerateArray()
                .ToDictionary(x => Str(x, "key")!, x => x.Clone());
            foreach (var i in chunk)
                results[i] = byKey.TryGetValue(keys[i]!, out var issue)
                    ? Map(urls[i], issue)
                    : Unavailable(urls[i]);
        }
        return results;
    }

    LinkPreview Map(Uri url, JsonElement issue)
    {
        var f = issue.GetProperty("fields");
        var category = Str(f, "status", "statusCategory", "key");
        var statusName = Str(f, "status", "name") ?? "";
        var assignee = Str(f, "assignee", "displayName");

        var state = category switch
        {
            "done" => LinkState.Done,
            _ when statusName.Contains("block", StringComparison.OrdinalIgnoreCase) =>
                LinkState.Blocked,
            _ when statusName.Contains("review", StringComparison.OrdinalIgnoreCase) =>
                LinkState.InReview,
            "indeterminate" => LinkState.InProgress,
            _ => LinkState.Open,
        };

        // Status name moves (Blocked → In Progress) matter for live links, so the normalized state is hashed too.
        var p = New(url, Str(f, "summary") ?? "", state, category, assignee);
        p.Subtitle = Str(issue, "key");
        p.ChipFacts = new List<ChipFact> { new(statusName) }
            .Concat(assignee is null ? [] : [new ChipFact("@" + assignee)])
            .ToList();
        p.AuthorName = assignee;
        p.Snippet = Str(f, "priority", "name") is { } prio ? $"Priority {prio}" : null;
        Meta(p, "kind", "issue");
        Meta(p, "number", Str(issue, "key"));
        Meta(p, "status", statusName);
        Meta(p, "story_points", Str(f, "customfield_10016"));
        return p;
    }
}
