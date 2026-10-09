using System.Text.Json;
using Noto.Core.Links;

namespace Noto.Providers.Providers;

public sealed class GitLabProvider : ProviderBase
{
    public override string ProviderId => "gitlab";
    public override string DisplayName => "GitLab";
    public override IReadOnlyList<AuthMethod> SupportedAuthMethods =>
        [AuthMethod.PersonalToken, AuthMethod.OAuth2];
    public override bool AcceptsSiteAddress => true;

    // The server's browser flow targets gitlab.com; a self-hosted GitLab signs in with a token.
    public override bool OAuthNeedsInstance => true;

    public override IReadOnlyList<UrlPattern> UrlPatterns { get; } =
    [
        new("gitlab.com", "/{path*}/-/merge_requests/{n}*"),
        new("gitlab.com", "/{path*}/-/issues/{n}*"),
    ];

    public override AuthConfig GetAuthConfig() =>
        new(
            "https://gitlab.com/oauth/authorize",
            "https://gitlab.com/oauth/token",
            ["read_api"],
            "https://gitlab.com/-/user_settings/personal_access_tokens"
        );

    public override Uri ApiRoot(string? instanceUrl) =>
        new($"{(instanceUrl ?? "https://gitlab.com").TrimEnd('/')}/api/v4/");

    public override TimeSpan CacheTtl(Uri url) =>
        url.AbsolutePath.Contains("/merge_requests/")
            ? TimeSpan.FromMinutes(5)
            : TimeSpan.FromMinutes(15);

    public override bool LooksLikeOwn(Uri url) =>
        url.AbsolutePath.Contains("/-/merge_requests/") || url.AbsolutePath.Contains("/-/issues/");

    public override IEnumerable<KeyValuePair<string, string>> AuthHeaders(
        Credential c,
        AuthMethod method
    ) => [method == AuthMethod.PersonalToken ? new("PRIVATE-TOKEN", c.AccessToken) : Bearer(c)];

    public override async Task<ConnectionIdentity> ValidateAsync(
        IProviderHttp http,
        CancellationToken ct
    )
    {
        using var doc = await http.GetJsonAsync(ProviderRequest.Get("user"), ct);
        return new ConnectionIdentity(Str(doc.RootElement, "username") ?? "GitLab");
    }

    public override async Task<LinkPreview> FetchAsync(
        Uri url,
        IProviderHttp http,
        CancellationToken ct
    )
    {
        var path = url.AbsolutePath;
        var isMr = path.Contains("/-/merge_requests/");
        var kind = isMr ? "merge_requests" : "issues";
        var split = path.Split($"/-/{kind}/", 2);
        var project = Uri.EscapeDataString(split[0].Trim('/'));
        var iid = new string(split[1].TakeWhile(char.IsDigit).ToArray());

        using var doc = await http.GetJsonAsync(
            ProviderRequest.Get($"projects/{project}/{kind}/{iid}"),
            ct
        );
        var n = doc.RootElement;
        var gitlabState = Str(n, "state");
        var assignee = n.TryGetProperty("assignees", out var a)
            ? a.EnumerateArray().Select(x => Str(x, "username")).FirstOrDefault()
            : null;

        if (!isMr)
        {
            var closed = gitlabState == "closed";
            var issue = New(
                url,
                Str(n, "title") ?? "",
                closed ? LinkState.Closed : LinkState.Open,
                gitlabState,
                assignee
            );
            issue.Subtitle = $"{split[0].Trim('/')}#{iid}";
            issue.ChipFacts = new List<ChipFact> { new(closed ? "closed" : "open") }
                .Concat(assignee is null ? [] : [new ChipFact("@" + assignee)])
                .Append(new($"{Str(n, "user_notes_count") ?? "0"} comments"))
                .ToList();
            issue.AuthorName = Str(n, "author", "name");
            issue.AuthorAvatarUrl = Str(n, "author", "avatar_url");
            Meta(issue, "kind", "issue");
            Meta(issue, "number", iid);
            return issue;
        }

        var approvals = 0;
        try
        {
            using var ap = await http.GetJsonAsync(
                ProviderRequest.Get($"projects/{project}/merge_requests/{iid}/approvals"),
                ct
            );
            approvals = ap.RootElement.TryGetProperty("approved_by", out var by)
                ? by.GetArrayLength()
                : 0;
        }
        catch (ProviderHttpException e) when (e.Status is 403 or 404) { } // approvals are a paid feature on some tiers

        var merged = gitlabState == "merged";
        var pipeline = Str(n, "head_pipeline", "status");
        var state =
            merged ? LinkState.Done
            : gitlabState == "closed" ? LinkState.Closed
            : Str(n, "draft") == "true" ? LinkState.InProgress
            : LinkState.InReview;

        var p = New(
            url,
            Str(n, "title") ?? "",
            state,
            gitlabState,
            merged.ToString(),
            approvals.ToString(),
            pipeline
        );
        p.Subtitle = $"{split[0].Trim('/')}!{iid}";
        var facts = new List<ChipFact>();
        if (approvals > 0)
            facts.Add(new($"{approvals} approved"));
        if (pipeline is not null)
            facts.Add(
                new(
                    pipeline switch
                    {
                        "success" => "CI passed",
                        "failed" => "CI failed",
                        _ => "CI pending",
                    }
                )
            );
        facts.Add(
            new(
                merged ? "merged"
                : gitlabState == "closed" ? "closed"
                : Str(n, "has_conflicts") == "true" ? "conflicts"
                : "open"
            )
        );
        p.ChipFacts = facts.Take(3).ToList();
        p.AuthorName = Str(n, "author", "name");
        p.AuthorAvatarUrl = Str(n, "author", "avatar_url");
        Meta(p, "kind", "pr");
        Meta(p, "number", iid);
        Meta(p, "merged", merged.ToString().ToLowerInvariant());
        return p;
    }
}
