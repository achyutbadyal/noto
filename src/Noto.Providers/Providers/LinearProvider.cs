using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Noto.Core.Links;

namespace Noto.Providers.Providers;

public sealed partial class LinearProvider : ProviderBase
{
    public override string ProviderId => "linear";
    public override string DisplayName => "Linear";
    public override IReadOnlyList<AuthMethod> SupportedAuthMethods => [AuthMethod.ApiKey, AuthMethod.OAuth2];
    public override IReadOnlyList<UrlPattern> UrlPatterns { get; } = [new("linear.app", "/{team}/issue/{id}*")];

    public override AuthConfig GetAuthConfig() => new(
        "https://linear.app/oauth/authorize", "https://api.linear.app/oauth/token", ["read"], "https://linear.app/settings/account/security");

    public override Uri ApiRoot(string? instanceUrl) => new("https://api.linear.app/");
    public override TimeSpan CacheTtl(Uri url) => TimeSpan.FromMinutes(10);

    // Personal API keys go in the header as-is; OAuth tokens are bearer.
    public override IEnumerable<KeyValuePair<string, string>> AuthHeaders(Credential c, AuthMethod method) =>
        [method == AuthMethod.ApiKey ? new("Authorization", c.AccessToken) : Bearer(c)];

    public override async Task<ConnectionIdentity> ValidateAsync(IProviderHttp http, CancellationToken ct)
    {
        var data = await GraphQlAsync(http, "graphql", "query { viewer { name email } }", ct);
        return new ConnectionIdentity(Str(data, "viewer", "name") ?? Str(data, "viewer", "email") ?? "Linear");
    }

    public override async Task<LinkPreview> FetchAsync(Uri url, IProviderHttp http, CancellationToken ct) =>
        (await FetchBatchAsync([url], http, ct))[0];

    public override async Task<IReadOnlyList<LinkPreview>> FetchBatchAsync(IReadOnlyList<Uri> urls, IProviderHttp http, CancellationToken ct)
    {
        var ids = urls.Select(IdOf).ToList();
        var results = new LinkPreview[urls.Count];
        var valid = Enumerable.Range(0, urls.Count).Where(i => ids[i] is not null).ToList();
        foreach (var i in Enumerable.Range(0, urls.Count).Except(valid)) results[i] = Unavailable(urls[i], "Unsupported Linear URL");

        foreach (var chunk in valid.Chunk(50))
        {
            var sb = new StringBuilder("query {");
            for (var n = 0; n < chunk.Length; n++)
                sb.Append($" i{n}: issue(id: \"{ids[chunk[n]]}\") {{ identifier title url description estimate state {{ name type }} assignee {{ name avatarUrl }} cycle {{ number }} labels {{ nodes {{ name }} }} }}");
            var data = await GraphQlAsync(http, "graphql", sb.Append(" }").ToString(), ct);

            for (var n = 0; n < chunk.Length; n++)
                results[chunk[n]] = Obj(data, $"i{n}") is { } issue ? Map(urls[chunk[n]], issue) : Unavailable(urls[chunk[n]]);
        }
        return results;
    }

    [GeneratedRegex(@"^[A-Za-z0-9]+-\d+$")] private static partial Regex IssueId();

    string? IdOf(Uri url) =>
        UrlPatterns[0].TryMatch(url, out var c) && IssueId().IsMatch(c["id"]) ? c["id"].ToUpperInvariant() : null;

    LinkPreview Map(Uri url, JsonElement i)
    {
        var type = Str(i, "state", "type");
        var name = Str(i, "state", "name") ?? "";
        var assignee = Str(i, "assignee", "name");

        var state = type switch
        {
            "completed" => LinkState.Done,
            "canceled" => LinkState.Closed,
            "started" when name.Contains("review", StringComparison.OrdinalIgnoreCase) => LinkState.InReview,
            "started" => LinkState.InProgress,
            _ when name.Contains("block", StringComparison.OrdinalIgnoreCase) => LinkState.Blocked,
            _ => LinkState.Open,
        };

        var p = New(url, Str(i, "title") ?? "", state, type, assignee);
        p.Subtitle = Str(i, "identifier");
        p.ChipFacts = new List<ChipFact> { new(name) }
            .Concat(assignee is null ? [] : [new ChipFact("@" + assignee)])
            .Concat(Str(i, "cycle", "number") is { } cycle ? [new ChipFact($"cycle {cycle}")] : []).ToList();
        p.AuthorName = assignee;
        p.AuthorAvatarUrl = Str(i, "assignee", "avatarUrl");
        var labels = i.TryGetProperty("labels", out var l) ? string.Join(", ", l.GetProperty("nodes").EnumerateArray().Select(x => Str(x, "name"))) : "";
        p.Snippet = labels.Length > 0 ? labels : null;
        Meta(p, "kind", "issue");
        Meta(p, "number", Str(i, "identifier"));
        Meta(p, "estimate", Str(i, "estimate"));
        return p;
    }
}
