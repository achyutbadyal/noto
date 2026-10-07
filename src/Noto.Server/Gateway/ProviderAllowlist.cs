using System.Text;
using Noto.Server.Config;
using Noto.Server.Middleware;

namespace Noto.Server.Gateway;

public sealed record AllowedRequest(Uri Target, bool IsGraphQl);

// Decides which upstream URL a gateway request may reach. GET only, except read-only GraphQL on the
// providers that expose a GraphQL endpoint. The client never chooses a host outside the provider's list.
public sealed class ProviderAllowlist(ServerConfig config)
{
    public AllowedRequest Resolve(
        ProviderDef provider,
        string? instanceUrl,
        string method,
        string path,
        IReadOnlyDictionary<string, string>? query,
        string? body
    )
    {
        var baseUri = BaseUri(provider, instanceUrl);
        ValidatePath(path);
        if (provider.PathPrefix is { } prefix && !path.StartsWith(prefix, StringComparison.Ordinal))
            throw ApiException.Forbidden("PATH_NOT_ALLOWED", "Path is outside the provider API");

        var target = new UriBuilder(baseUri) { Path = path, Query = BuildQuery(query) }.Uri;
        if (
            !string.Equals(target.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            || target.Scheme != baseUri.Scheme
        )
            throw ApiException.Forbidden("HOST_NOT_ALLOWED", "Request escapes the provider host");

        var isGraphQl =
            provider.GraphQlPath is not null
            && path == provider.GraphQlPath
            && string.Equals(target.Host, provider.GraphQlHost, StringComparison.OrdinalIgnoreCase);

        switch (method.ToUpperInvariant())
        {
            case "GET":
                return new AllowedRequest(target, false);
            case "POST" when isGraphQl:
                if (body is null || !GraphQlGuard.IsReadOnlyRequest(body))
                    throw ApiException.Forbidden(
                        "GRAPHQL_NOT_READ_ONLY",
                        "Only read-only GraphQL queries are allowed"
                    );
                return new AllowedRequest(target, true);
            default:
                throw ApiException.Forbidden(
                    "METHOD_NOT_ALLOWED",
                    "The gateway is read-only (GET, or read-only GraphQL)"
                );
        }
    }

    // Redirects may only stay on the original host, the provider's own hosts, or an operator-allowed host.
    public bool IsAllowedRedirect(ProviderDef provider, Uri original, Uri next) =>
        string.Equals(original.Host, next.Host, StringComparison.OrdinalIgnoreCase)
        || provider.Hosts.Any(h => HostPattern.Matches(h, next.Host))
        || config.AllowPrivateHosts.Contains(next.Host);

    Uri BaseUri(ProviderDef provider, string? instanceUrl)
    {
        if (string.IsNullOrWhiteSpace(instanceUrl))
        {
            var host =
                provider.Hosts.FirstOrDefault(h => !h.StartsWith('*'))
                ?? throw ApiException.BadRequest(
                    "INSTANCE_URL_REQUIRED",
                    "instance_url is required for this provider"
                );
            return new Uri($"https://{host}");
        }

        if (
            !Uri.TryCreate(instanceUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")
            || !string.IsNullOrEmpty(uri.UserInfo)
        )
            throw ApiException.BadRequest(
                "INVALID_INSTANCE_URL",
                "instance_url must be an absolute https URL"
            );

        var known = provider.Hosts.Any(h => HostPattern.Matches(h, uri.Host));
        var operatorAllowed = config.AllowPrivateHosts.Contains(uri.Host);
        if (!known && !operatorAllowed && !provider.AllowInstanceUrl)
            throw ApiException.Forbidden(
                "HOST_NOT_ALLOWED",
                "Host is not allowlisted for this provider"
            );
        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }

    static void ValidatePath(string path)
    {
        var decoded = Uri.UnescapeDataString(path);
        if (
            !path.StartsWith('/')
            || path.StartsWith("//")
            || decoded.Contains('\\')
            || decoded.Contains("..")
            || path.Contains("://")
            || path.Any(char.IsControl)
        )
            throw ApiException.BadRequest("INVALID_PATH", "Invalid request path");
    }

    static string BuildQuery(IReadOnlyDictionary<string, string>? query)
    {
        if (query is null || query.Count == 0)
            return "";
        var sb = new StringBuilder();
        foreach (var (k, v) in query)
        {
            if (sb.Length > 0)
                sb.Append('&');
            sb.Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v));
        }
        return sb.ToString();
    }
}
