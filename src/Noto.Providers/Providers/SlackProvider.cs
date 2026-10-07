using System.Text.Json;
using Noto.Core.Links;
using Noto.Providers.Auth;

namespace Noto.Providers.Providers;

public sealed record SlackOptions(bool TreatCheckReactionAsResolved = false);

// Thrown when the workspace requires an admin to approve the Noto Slack app.
public sealed class ApprovalRequiredException(string workspace)
    : AuthRequiredException($"Slack admin approval needed for {workspace}")
{
    public string RequestMessage { get; } =
        $"Hi! I'd like to connect Noto (a todo app) to our Slack workspace ({workspace}) so it can show thread status on my todos. "
        + "It only reads threads I link to. Could you approve the app? Thanks!";
}

public sealed class SlackProvider(SlackOptions? options = null) : ProviderBase
{
    // Error codes treated as "an admin has to approve this app". Not verified against a live workspace.
    static readonly HashSet<string> ApprovalErrors =
    [
        "admin_approval_required",
        "app_approval_required",
        "request_pending",
    ];
    static readonly HashSet<string> MissingErrors =
    [
        "channel_not_found",
        "thread_not_found",
        "message_not_found",
    ];
    static readonly HashSet<string> AuthErrors =
    [
        "invalid_auth",
        "token_revoked",
        "token_expired",
        "not_authed",
        "account_inactive",
    ];

    readonly SlackOptions _options = options ?? new();

    public override string ProviderId => "slack";
    public override string DisplayName => "Slack";
    public override bool IsInstanceBased => true;
    public override IReadOnlyList<AuthMethod> SupportedAuthMethods =>
        [AuthMethod.OAuth2, AuthMethod.PersonalToken];
    public override IReadOnlyList<UrlPattern> UrlPatterns { get; } =
    [new("{team}.slack.com", "/archives/{channel}/p{ts}")];

    public override AuthConfig GetAuthConfig() =>
        new(
            "https://slack.com/oauth/v2/authorize",
            "https://slack.com/api/oauth.v2.access",
            ["channels:history", "groups:history", "channels:read", "groups:read", "users:read"],
            "https://api.slack.com/authentication/token-types",
            UsesPkce: false,
            RequiresClientSecret: true,
            ScopeParam: "user_scope"
        );

    public override Uri ApiRoot(string? instanceUrl) => new("https://slack.com/api/");

    public override TimeSpan CacheTtl(Uri url) => TimeSpan.FromMinutes(5);

    public override bool LooksLikeOwn(Uri url) =>
        url.IdnHost.EndsWith(".slack.com", StringComparison.OrdinalIgnoreCase)
        && url.AbsolutePath.StartsWith("/archives/");

    public override async Task<ConnectionIdentity> ValidateAsync(
        IProviderHttp http,
        CancellationToken ct
    )
    {
        using var doc = await CallAsync(http, "auth.test", [], null, ct);
        return new ConnectionIdentity(Str(doc.RootElement, "team") ?? "Slack");
    }

    public override async Task<bool> RevokeAsync(IProviderHttp http, CancellationToken ct)
    {
        using var doc = await http.GetJsonAsync(ProviderRequest.Get("auth.revoke"), ct);
        return Str(doc.RootElement, "revoked") == "true";
    }

    public override async Task<LinkPreview> FetchAsync(
        Uri url,
        IProviderHttp http,
        CancellationToken ct
    )
    {
        if (!UrlPatterns[0].TryMatch(url, out var c))
            return Unavailable(url, "Unsupported Slack URL");
        var team = c["team"];
        var messageTs = ToTs(c["ts"]);
        var threadTs = Query(url, "thread_ts") ?? messageTs;

        using var replies = await CallAsync(
            http,
            "conversations.replies",
            [("channel", c["channel"]), ("ts", threadTs), ("limit", "100")],
            team,
            ct
        );
        var messages = replies
            .RootElement.GetProperty("messages")
            .EnumerateArray()
            .Select(m => m.Clone())
            .ToList();
        if (messages.Count == 0)
            return Unavailable(url);

        var parent = messages[0];
        var replyCount = messages.Count - 1;
        var latestTs = Str(messages[^1], "ts") ?? threadTs;
        var resolved = _options.TreatCheckReactionAsResolved && HasCheckReaction(parent);

        var channelName = await TryAsync(
            http,
            "conversations.info",
            [("channel", c["channel"])],
            d => Str(d.RootElement, "channel", "name"),
            team,
            ct
        );
        var author = Str(parent, "user") is { } uid
            ? await TryAsync(
                http,
                "users.info",
                [("user", uid)],
                d => Str(d.RootElement, "user", "real_name") ?? Str(d.RootElement, "user", "name"),
                team,
                ct
            )
            : null;

        // No native "resolved" state: only the opt-in check-mark reaction maps to Done.
        var p = New(
            url,
            Excerpt(Str(parent, "text") ?? "Slack message"),
            resolved ? LinkState.Done : LinkState.Unknown,
            replyCount.ToString(),
            latestTs
        );
        p.Subtitle = channelName is null ? team : $"#{channelName}";
        p.ChipFacts = new List<ChipFact>
        {
            new(p.Subtitle),
            new($"{replyCount} {(replyCount == 1 ? "reply" : "replies")}"),
        };
        p.AuthorName = author;
        p.Snippet = Excerpt(Str(parent, "text") ?? "", 280);
        Meta(p, "kind", "thread");
        Meta(p, "latest_reply_ts", latestTs);
        Meta(p, "reply_count", replyCount.ToString());
        return p;
    }

    // Slack reports failures as HTTP 200 with ok:false; translate them to the shared failure vocabulary.
    async Task<JsonDocument> CallAsync(
        IProviderHttp http,
        string method,
        (string, string)[] query,
        string? team,
        CancellationToken ct
    )
    {
        var doc = await http.GetJsonAsync(ProviderRequest.Get(method, query), ct);
        if (Str(doc.RootElement, "ok") == "true")
            return doc;

        var error = Str(doc.RootElement, "error") ?? "unknown";
        doc.Dispose();
        if (ApprovalErrors.Contains(error))
            throw new ApprovalRequiredException(team ?? "your workspace");
        if (MissingErrors.Contains(error))
            throw new ProviderHttpException(404, error: error);
        if (AuthErrors.Contains(error))
            throw new ProviderHttpException(401, error: error);
        if (error == "not_in_channel")
            throw new ProviderHttpException(403, error: error);
        if (error == "ratelimited")
            throw new ProviderHttpException(429, TimeSpan.FromSeconds(30), error);
        throw new ProviderHttpException(502, error: error);
    }

    async Task<T?> TryAsync<T>(
        IProviderHttp http,
        string method,
        (string, string)[] query,
        Func<JsonDocument, T?> read,
        string? team,
        CancellationToken ct
    )
    {
        try
        {
            using var doc = await CallAsync(http, method, query, team, ct);
            return read(doc);
        }
        catch (ProviderHttpException)
        {
            return default;
        } // names are nice-to-have
    }

    static bool HasCheckReaction(JsonElement message) =>
        message.TryGetProperty("reactions", out var r)
        && r.EnumerateArray().Any(x => Str(x, "name") == "white_check_mark");

    // p1700000000123456 → 1700000000.123456
    static string ToTs(string p) => p.Length > 10 ? $"{p[..10]}.{p[10..]}" : p;

    static string? Query(Uri url, string key) =>
        url
            .Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2))
            .FirstOrDefault(x => x[0] == key)
            is { Length: 2 } kv
            ? Uri.UnescapeDataString(kv[1])
            : null;

    static string Excerpt(string text, int max = 80)
    {
        var single = text.ReplaceLineEndings(" ").Trim();
        return single.Length <= max ? single : single[..(max - 1)] + "…";
    }
}
