using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Noto.Core.Links;

namespace Noto.Providers.Providers;

public sealed partial class GitHubProvider : ProviderBase
{
    public const int BatchSize = 50;

    public override string ProviderId => "github";
    public override string DisplayName => "GitHub";
    public override IReadOnlyList<AuthMethod> SupportedAuthMethods =>
        [AuthMethod.PersonalToken, AuthMethod.OAuth2];
    public override bool AcceptsSiteAddress => true;

    public override IReadOnlyList<UrlPattern> UrlPatterns { get; } =
    [
        new("github.com", "/{owner}/{repo}/pull/{n}*"),
        new("github.com", "/{owner}/{repo}/issues/{n}*"),
        new("github.com", "/{owner}/{repo}/commit/{sha}*"),
    ];

    public override AuthConfig GetAuthConfig() =>
        new(
            "https://github.com/login/oauth/authorize",
            "https://github.com/login/oauth/access_token",
            ["repo", "read:user"],
            "https://github.com/settings/personal-access-tokens/new",
            RequiresClientSecret: true
        );

    public override Uri ApiRoot(string? instanceUrl) =>
        instanceUrl is null
            ? new("https://api.github.com/")
            : new($"{instanceUrl.TrimEnd('/')}/api/");

    public override TimeSpan CacheTtl(Uri url) =>
        Parse(url)?.Kind == Kind.Issue ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(5);

    public override async Task<ConnectionIdentity> ValidateAsync(
        IProviderHttp http,
        CancellationToken ct
    )
    {
        // GHES serves REST under /api/v3, github.com at the root; try both shapes.
        foreach (var path in new[] { "user", "v3/user" })
        {
            try
            {
                using var doc = await http.GetJsonAsync(ProviderRequest.Get(path), ct);
                return new ConnectionIdentity(Str(doc.RootElement, "login") ?? "GitHub");
            }
            catch (ProviderHttpException e) when (e.Status == 404) { }
        }
        throw new ProviderHttpException(404);
    }

    public override async Task<LinkPreview> FetchAsync(
        Uri url,
        IProviderHttp http,
        CancellationToken ct
    ) => (await FetchBatchAsync([url], http, ct))[0];

    // One GraphQL query per 50 objects, one alias per object.
    public override async Task<IReadOnlyList<LinkPreview>> FetchBatchAsync(
        IReadOnlyList<Uri> urls,
        IProviderHttp http,
        CancellationToken ct
    )
    {
        var results = new LinkPreview[urls.Count];
        foreach (var chunk in Enumerable.Range(0, urls.Count).Chunk(BatchSize))
        {
            var refs = chunk.Select(i => (Index: i, Ref: Parse(urls[i]))).ToList();
            foreach (var bad in refs.Where(r => r.Ref is null))
                results[bad.Index] = Unavailable(urls[bad.Index], "Unsupported GitHub URL");
            var good = refs.Where(r => r.Ref is not null).ToList();
            if (good.Count == 0)
                continue;

            var data = await GraphQlAsync(
                http,
                "graphql",
                BuildQuery(good.Select(g => g.Ref!).ToList()),
                ct
            );
            for (var n = 0; n < good.Count; n++)
            {
                var url = urls[good[n].Index];
                results[good[n].Index] =
                    data.TryGetProperty($"r{n}", out var repo)
                    && Obj(repo, good[n].Ref!.Field) is { } node
                        ? Map(url, good[n].Ref!, node)
                        : Unavailable(url);
            }
        }
        return results;
    }

    enum Kind
    {
        Pr,
        Issue,
        Commit,
    }

    sealed record Ref(Kind Kind, string Owner, string Repo, string Id)
    {
        public string Field =>
            Kind switch
            {
                Kind.Pr => "pullRequest",
                Kind.Issue => "issue",
                _ => "object",
            };
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+$")]
    private static partial Regex Safe();

    Ref? Parse(Uri url)
    {
        var parts = url.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 4 || !Safe().IsMatch(parts[0]) || !Safe().IsMatch(parts[1]))
            return null;
        var kind = parts[2] switch
        {
            "pull" => Kind.Pr,
            "issues" => Kind.Issue,
            "commit" => Kind.Commit,
            _ => (Kind?)null,
        };
        return kind is { } k && Safe().IsMatch(parts[3])
            ? new Ref(k, parts[0], parts[1], parts[3])
            : null;
    }

    // Owner/repo/id are validated against a strict charset above, so inlining them cannot break out of the string.
    static string BuildQuery(IReadOnlyList<Ref> refs)
    {
        var sb = new StringBuilder("query {");
        for (var i = 0; i < refs.Count; i++)
        {
            var r = refs[i];
            var inner = r.Kind switch
            {
                Kind.Pr =>
                    $"pullRequest(number: {int.Parse(r.Id)}) {{ title state merged isDraft mergeable reviewDecision additions deletions changedFiles url "
                        + "author { login avatarUrl } latestOpinionatedReviews(first: 50) { nodes { state } } "
                        + "commits(last: 1) { nodes { commit { statusCheckRollup { state } } } } "
                        + "reviewRequests(first: 10) { nodes { requestedReviewer { ... on User { login } } } } }",
                Kind.Issue =>
                    $"issue(number: {int.Parse(r.Id)}) {{ title state url bodyText author {{ login avatarUrl }} "
                        + "assignees(first: 3) { nodes { login } } comments { totalCount } labels(first: 5) { nodes { name } } }",
                _ =>
                    $"object(expression: \"{r.Id}\") {{ ... on Commit {{ messageHeadline oid url author {{ name }} }} }}",
            };
            sb.Append($" r{i}: repository(owner: \"{r.Owner}\", name: \"{r.Repo}\") {{ {inner} }}");
        }
        return sb.Append(" }").ToString();
    }

    LinkPreview Map(Uri url, Ref r, JsonElement n)
    {
        var subtitle = $"{r.Owner}/{r.Repo}";
        LinkPreview p;

        if (r.Kind == Kind.Pr)
        {
            var merged = Str(n, "merged") == "true";
            var closed = Str(n, "state") == "CLOSED";
            var decision = Str(n, "reviewDecision");
            var approvals = n.TryGetProperty("latestOpinionatedReviews", out var rv)
                ? rv.GetProperty("nodes").EnumerateArray().Count(x => Str(x, "state") == "APPROVED")
                : 0;
            var ci = CiConclusion(n);

            var state =
                merged ? LinkState.Done
                : closed ? LinkState.Closed
                : decision == "CHANGES_REQUESTED" ? LinkState.Blocked
                : decision == "REVIEW_REQUIRED" ? LinkState.InReview
                : LinkState.Open;

            p = New(
                url,
                Str(n, "title") ?? "",
                state,
                Str(n, "state"),
                merged.ToString(),
                decision,
                ci
            );
            p.Subtitle = $"{subtitle} #{r.Id}";
            var facts = new List<ChipFact>();
            if (approvals > 0)
                facts.Add(new($"{approvals} approved"));
            if (ci is not null)
                facts.Add(
                    new(
                        ci switch
                        {
                            "SUCCESS" => "CI passed",
                            "FAILURE" or "ERROR" => "CI failed",
                            _ => "CI pending",
                        }
                    )
                );
            facts.Add(
                new(
                    merged ? "merged"
                    : closed ? "closed"
                    : Str(n, "isDraft") == "true" ? "draft"
                    : Str(n, "mergeable") == "CONFLICTING" ? "conflicts"
                    : "ready"
                )
            );
            p.ChipFacts = facts.Take(3).ToList();
            p.Snippet =
                $"+{Str(n, "additions")} / −{Str(n, "deletions")} · {Str(n, "changedFiles")} files";
            Meta(p, "kind", "pr");
            Meta(p, "merged", merged.ToString().ToLowerInvariant());
        }
        else if (r.Kind == Kind.Issue)
        {
            var closed = Str(n, "state") == "CLOSED";
            var assignee = n.TryGetProperty("assignees", out var a)
                ? a.GetProperty("nodes")
                    .EnumerateArray()
                    .Select(x => Str(x, "login"))
                    .FirstOrDefault()
                : null;
            p = New(
                url,
                Str(n, "title") ?? "",
                closed ? LinkState.Closed : LinkState.Open,
                Str(n, "state"),
                assignee
            );
            p.Subtitle = $"{subtitle} #{r.Id}";
            p.ChipFacts = new List<ChipFact> { new(closed ? "closed" : "open") }
                .Concat(assignee is null ? [] : [new ChipFact("@" + assignee)])
                .Append(new($"{Str(n, "comments", "totalCount") ?? "0"} comments"))
                .ToList();
            p.Snippet = Str(n, "bodyText") is { } body
                ? (body.Length > 200 ? body[..200] : body)
                : null;
            Meta(p, "kind", "issue");
        }
        else
        {
            p = New(url, Str(n, "messageHeadline") ?? r.Id, null, Str(n, "oid"));
            p.Subtitle = $"{subtitle}@{r.Id[..Math.Min(7, r.Id.Length)]}";
            Meta(p, "kind", "commit");
        }

        p.AuthorName = Str(n, "author", "login") ?? Str(n, "author", "name");
        p.AuthorAvatarUrl = Str(n, "author", "avatarUrl");
        Meta(p, "number", r.Id);
        return p;
    }

    static string? CiConclusion(JsonElement pr) =>
        pr.TryGetProperty("commits", out var c)
        && c.GetProperty("nodes").EnumerateArray().FirstOrDefault()
            is { ValueKind: JsonValueKind.Object } node
            ? Str(node, "commit", "statusCheckRollup", "state")
            : null;
}
