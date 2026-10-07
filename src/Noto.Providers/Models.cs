using System.Text.Json;
using System.Text.RegularExpressions;
using Noto.Core.Links;

namespace Noto.Providers;

// Host may be "*.atlassian.net" or "{team}.slack.com"; path "/{o}/{r}/pull/{n}" with a trailing "*" for "anything".
public sealed record UrlPattern(string Host, string Path)
{
    public bool TryMatch(Uri url, out IReadOnlyDictionary<string, string> captures)
    {
        captures = new Dictionary<string, string>();
        var host = Regex.Match(
            url.IdnHost.ToLowerInvariant(),
            "^" + ToRegex(Host, hostMode: true) + "$"
        );
        var path = Regex.Match(
            url.AbsolutePath,
            "^" + ToRegex(Path, hostMode: false) + "/?$",
            RegexOptions.IgnoreCase
        );
        if (!host.Success || !path.Success)
            return false;

        var map = new Dictionary<string, string>();
        foreach (var g in host.Groups.Cast<Group>().Concat(path.Groups.Cast<Group>()))
            if (g.Success && !int.TryParse(g.Name, out _))
                map[g.Name] = Uri.UnescapeDataString(g.Value);
        captures = map;
        return true;
    }

    static string ToRegex(string pattern, bool hostMode)
    {
        var segment = hostMode ? "[^./]+" : "[^/]+";
        var escaped = Regex.Escape(pattern).Replace(@"\{", "{").Replace(@"\*", "*");
        escaped = Regex.Replace(escaped, @"\{(\w+)\*\}", "(?<$1>.+)"); // {path*} spans slashes
        escaped = Regex.Replace(escaped, @"\{(\w+)\}", $"(?<$1>{segment})");
        // "*." leads a host wildcard label; any other "*" swallows the rest.
        return hostMode ? escaped.Replace("*", "[^./]+") : escaped.Replace("*", ".*");
    }
}

public sealed record AuthConfig(
    string? AuthorizeUrl,
    string? TokenUrl,
    IReadOnlyList<string> Scopes,
    string PatHelpUrl,
    bool UsesPkce = true,
    bool RequiresClientSecret = false,
    string ScopeParam = "scope"
);

public sealed record ConnectionIdentity(string DisplayLabel, IReadOnlyList<string>? Scopes = null);

public sealed record Credential(
    string AccessToken,
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAt = null,
    string? Username = null
);

// Provider-relative request: transports resolve it against the provider's API root (or a gateway).
public sealed record ProviderRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string>? Query = null,
    string? JsonBody = null,
    IReadOnlyDictionary<string, string>? Headers = null
)
{
    public static ProviderRequest Get(string path, params (string Key, string Value)[] query) =>
        new("GET", path, query.Length == 0 ? null : query.ToDictionary(q => q.Key, q => q.Value));

    public static ProviderRequest Post(string path, string jsonBody) =>
        new("POST", path, null, jsonBody);
}

public sealed record ProviderResponse(
    int Status,
    string Body,
    IReadOnlyDictionary<string, string> Headers
)
{
    public bool IsSuccess => Status is >= 200 and < 300;
}

public sealed record OpenGraphData(
    string? Title,
    string? Description,
    string? Image,
    string? SiteName
);

public interface IProviderHttp
{
    Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct);
    Task<OpenGraphData?> OpenGraphAsync(Uri url, CancellationToken ct);
}

// Deliberately carries no response body: bodies are third-party content and must not reach logs.
public sealed class ProviderHttpException(
    int status,
    TimeSpan? retryAfter = null,
    string? error = null
) : Exception($"Provider returned {status}{(error is null ? "" : $" ({error})")}")
{
    public int Status { get; } = status;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public string? Error { get; } = error;
}

public static class ProviderHttpExtensions
{
    public static async Task<JsonDocument> GetJsonAsync(
        this IProviderHttp http,
        ProviderRequest request,
        CancellationToken ct
    )
    {
        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccess)
        {
            TimeSpan? retry =
                response.Headers.TryGetValue("Retry-After", out var v)
                && double.TryParse(v, out var s)
                    ? TimeSpan.FromSeconds(s)
                    : null;
            throw new ProviderHttpException(response.Status, retry);
        }
        return JsonDocument.Parse(response.Body);
    }
}

public interface IAppProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    string IconPath { get; }
    IReadOnlyList<UrlPattern> UrlPatterns { get; }
    IReadOnlyList<AuthMethod> SupportedAuthMethods { get; }
    AuthConfig GetAuthConfig();

    // True when the host is chosen by the connection (self-hosted / per-tenant) rather than fixed.
    bool IsInstanceBased { get; }

    // `connection` resolves instance hosts; null means "match by the provider's fixed hosts only".
    bool CanHandle(Uri url, AppConnection? connection);

    // Lets the registry hint "Connect Jira (jira.acme.com)" for an unknown host.
    bool LooksLikeOwn(Uri url);

    Uri ApiRoot(string? instanceUrl);
    IEnumerable<KeyValuePair<string, string>> AuthHeaders(Credential credential, AuthMethod method);

    // Credentials sent in the query string (custom apps with an API-key param). Direct transport only.
    IEnumerable<KeyValuePair<string, string>> AuthQuery(Credential credential, AuthMethod method);
    TimeSpan CacheTtl(Uri url);

    Task<ConnectionIdentity> ValidateAsync(IProviderHttp http, CancellationToken ct);
    Task<LinkPreview> FetchAsync(Uri url, IProviderHttp http, CancellationToken ct);

    // Same order and length as `urls`; objects that no longer exist come back as Unavailable previews.
    Task<IReadOnlyList<LinkPreview>> FetchBatchAsync(
        IReadOnlyList<Uri> urls,
        IProviderHttp http,
        CancellationToken ct
    );
    Task<bool> RevokeAsync(IProviderHttp http, CancellationToken ct);
}
