using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Noto.Core.Links;

namespace Noto.Providers;

public abstract class ProviderBase : IAppProvider
{
    public abstract string ProviderId { get; }
    public abstract string DisplayName { get; }
    public virtual string IconPath => $"icons/{ProviderId}.svg";
    public abstract IReadOnlyList<UrlPattern> UrlPatterns { get; }
    public abstract IReadOnlyList<AuthMethod> SupportedAuthMethods { get; }
    public abstract AuthConfig GetAuthConfig();
    public virtual bool IsInstanceBased => false;

    public virtual bool CanHandle(Uri url, AppConnection? connection)
    {
        if (connection?.InstanceUrl is { } instance)
        {
            if (!Uri.TryCreate(instance, UriKind.Absolute, out var inst)) return false;
            return string.Equals(inst.IdnHost, url.IdnHost, StringComparison.OrdinalIgnoreCase)
                && UrlPatterns.Any(p => PathMatches(p, url));
        }
        return UrlPatterns.Any(p => p.TryMatch(url, out _));
    }

    public virtual bool LooksLikeOwn(Uri url) => false;

    public abstract Uri ApiRoot(string? instanceUrl);

    public virtual IEnumerable<KeyValuePair<string, string>> AuthHeaders(Credential credential, AuthMethod method) =>
        [Bearer(credential)];

    public virtual IEnumerable<KeyValuePair<string, string>> AuthQuery(Credential credential, AuthMethod method) => [];

    public abstract TimeSpan CacheTtl(Uri url);
    public abstract Task<ConnectionIdentity> ValidateAsync(IProviderHttp http, CancellationToken ct);
    public abstract Task<LinkPreview> FetchAsync(Uri url, IProviderHttp http, CancellationToken ct);

    public virtual async Task<IReadOnlyList<LinkPreview>> FetchBatchAsync(IReadOnlyList<Uri> urls, IProviderHttp http, CancellationToken ct)
    {
        var results = new List<LinkPreview>();
        foreach (var url in urls)
        {
            try { results.Add(await FetchAsync(url, http, ct)); }
            catch (ProviderHttpException e) when (e.Status is 404 or 410) { results.Add(Unavailable(url, e.Message)); }
        }
        return results;
    }

    public virtual Task<bool> RevokeAsync(IProviderHttp http, CancellationToken ct) => Task.FromResult(false);

    // Path-only check: the host was already vetted against the connection's instance URL.
    static bool PathMatches(UrlPattern p, Uri url) =>
        new UrlPattern("x.invalid", p.Path).TryMatch(new Uri($"https://x.invalid{url.AbsolutePath}"), out _);

    // ---- helpers shared by providers ----

    protected static KeyValuePair<string, string> Bearer(Credential c) => new("Authorization", $"Bearer {c.AccessToken}");

    protected static KeyValuePair<string, string> Basic(Credential c) =>
        new("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.Username}:{c.AccessToken}")));

    protected LinkPreview New(Uri url, string title, LinkState? state, params string?[] hashParts) => new()
    {
        Url = LinkUrl.Normalize(url.ToString()) ?? url.ToString(),
        ProviderId = ProviderId,
        Title = title,
        State = state,
        StateHash = hashParts.Length == 0 ? null : Hash(hashParts.Append(state?.ToString())),
        Status = PreviewStatus.Loaded,
    };

    protected LinkPreview Unavailable(Uri url, string? message = null) => new()
    {
        Url = LinkUrl.Normalize(url.ToString()) ?? url.ToString(),
        ProviderId = ProviderId,
        Title = url.Host,
        Status = PreviewStatus.Unavailable,
        ErrorMessage = message ?? "No longer accessible",
    };

    // Short, stable digest of the inputs that count as a meaningful change.
    protected static string Hash(IEnumerable<string?> parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)))).ToLowerInvariant()[..16];

    protected static string? Str(JsonElement e, params string[] path)
    {
        foreach (var key in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out e)) return null;
        }
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => e.GetRawText(),
            _ => null,
        };
    }

    protected static int? Int(JsonElement e, params string[] path) => int.TryParse(Str(e, path), out var n) ? n : null;

    protected static JsonElement? Obj(JsonElement e, params string[] path)
    {
        foreach (var key in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out e)) return null;
        }
        return e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : e;
    }

    protected static void Meta(LinkPreview p, string key, string? value)
    {
        if (value is not null)
            p.Metadata[key] = JsonDocument.Parse(JsonValue.Create(value)!.ToJsonString()).RootElement.Clone();
    }

    // Read-only GraphQL (the gateway rejects mutations). Returns `data`; throws when nothing usable came back.
    protected static async Task<JsonElement> GraphQlAsync(IProviderHttp http, string path, string query, CancellationToken ct)
    {
        var body = new JsonObject { ["query"] = query }.ToJsonString();
        var doc = await http.GetJsonAsync(ProviderRequest.Post(path, body), ct);
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object) return data.Clone();
        throw new ProviderHttpException(502, error: "graphql returned no data");
    }

    protected static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
