using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Noto.Core.Links;
using Noto.Providers.Auth;

namespace Noto.Providers.Transport;

public interface IProviderHttpFactory
{
    IProviderHttp Create(IAppProvider provider, string? instanceUrl, AuthMethod method, ICredentialSource credentials);
}

// Native clients: straight HTTPS to the provider, token from the keyring.
public sealed class DirectTransport(
    HttpClient http, IAppProvider provider, string? instanceUrl, AuthMethod method,
    ICredentialSource credentials, ILogger logger) : IProviderHttp
{
    public async Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct)
    {
        var credential = await credentials.GetAsync(ct);
        var authQuery = credential is null ? [] : provider.AuthQuery(credential, method).ToList();
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), Resolve(request, authQuery));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.UserAgent.ParseAdd("Noto/1.0");

        if (credential is not null)
            foreach (var (name, value) in provider.AuthHeaders(credential, method))
                message.Headers.TryAddWithoutValidation(name, value);
        foreach (var (name, value) in request.Headers ?? new Dictionary<string, string>())
            message.Headers.TryAddWithoutValidation(name, value);
        if (request.JsonBody is not null) message.Content = new StringContent(request.JsonBody, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        // Path only: custom apps may carry API keys in the query string.
        logger.LogDebug("{Provider} {Method} {Path} -> {Status}", provider.ProviderId, request.Method, request.Path.Split('?')[0], (int)response.StatusCode);

        var headers = response.Headers.Concat(response.Content.Headers).ToDictionary(h => h.Key, h => h.Value.First(), StringComparer.OrdinalIgnoreCase);
        return new ProviderResponse((int)response.StatusCode, body, headers);
    }

    public async Task<OpenGraphData?> OpenGraphAsync(Uri url, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; NotoLinkPreview/1.0)");
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return null;

        // Only the head matters; cap the read at 1 MB.
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[1 << 20];
        var read = 0;
        while (read < buffer.Length && await stream.ReadAsync(buffer.AsMemory(read), ct) is > 0 and var n) read += n;
        return OpenGraphParser.Parse(Encoding.UTF8.GetString(buffer, 0, read));
    }

    Uri Resolve(ProviderRequest request, IReadOnlyList<KeyValuePair<string, string>> authQuery)
    {
        var path = request.Path;
        var baseUri = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new Uri(path)
            : new Uri(provider.ApiRoot(instanceUrl), path.TrimStart('/'));
        var all = (request.Query ?? new Dictionary<string, string>()).Concat(authQuery).ToList();
        if (all.Count == 0) return baseUri;

        var q = string.Join('&', all.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var builder = new UriBuilder(baseUri);
        builder.Query = string.IsNullOrEmpty(builder.Query) ? q : builder.Query.TrimStart('?') + "&" + q;
        return builder.Uri;
    }
}

public sealed class DirectTransportFactory(HttpClient http, ILogger<DirectTransport> logger) : IProviderHttpFactory
{
    public IProviderHttp Create(IAppProvider provider, string? instanceUrl, AuthMethod method, ICredentialSource credentials) =>
        new DirectTransport(http, provider, instanceUrl, method, credentials, logger);
}

// Browser client: requests go through the Noto backend (docs/06 › Gateway). The provider token travels
// per request in X-Provider-Authorization and is never stored server-side.
public sealed class GatewayTransport(
    HttpClient http, Uri gatewayBase, Func<CancellationToken, Task<string>> notoAccessToken,
    IAppProvider provider, string? instanceUrl, AuthMethod method, ICredentialSource credentials) : IProviderHttp
{
    public async Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct)
    {
        var body = new System.Text.Json.Nodes.JsonObject
        {
            ["provider_id"] = provider.ProviderId,
            ["instance_url"] = instanceUrl,
            ["request"] = new System.Text.Json.Nodes.JsonObject
            {
                ["method"] = request.Method,
                ["path"] = request.Path,
                ["query"] = request.Query is null ? null : new System.Text.Json.Nodes.JsonObject(
                    request.Query.Select(kv => KeyValuePair.Create(kv.Key, (System.Text.Json.Nodes.JsonNode?)kv.Value))),
                ["body"] = request.JsonBody is null ? null : System.Text.Json.Nodes.JsonNode.Parse(request.JsonBody),
            },
        };

        using var message = await BuildAsync("gateway/fetch", body.ToJsonString(), ct);
        if (await credentials.GetAsync(ct) is { } credential)
            message.Headers.TryAddWithoutValidation("X-Provider-Authorization", provider.AuthHeaders(credential, method).First().Value);

        using var response = await http.SendAsync(message, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return new ProviderResponse((int)response.StatusCode, text, new Dictionary<string, string>());

        // Gateway envelope: {status, headers, body}
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var headers = root.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object
            ? h.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>();
        var inner = root.TryGetProperty("body", out var b) ? (b.ValueKind == JsonValueKind.String ? b.GetString()! : b.GetRawText()) : "";
        return new ProviderResponse(root.GetProperty("status").GetInt32(), inner, headers);
    }

    public async Task<OpenGraphData?> OpenGraphAsync(Uri url, CancellationToken ct)
    {
        using var message = await BuildAsync("gateway/opengraph", new System.Text.Json.Nodes.JsonObject { ["url"] = url.ToString() }.ToJsonString(), ct);
        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var r = doc.RootElement;
        string? Get(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return new OpenGraphData(Get("title"), Get("description"), Get("image"), Get("site_name"));
    }

    async Task<HttpRequestMessage> BuildAsync(string path, string json, CancellationToken ct)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, new Uri(gatewayBase, path))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await notoAccessToken(ct));
        return message;
    }
}

public sealed class GatewayTransportFactory(HttpClient http, Uri gatewayBase, Func<CancellationToken, Task<string>> notoAccessToken) : IProviderHttpFactory
{
    public IProviderHttp Create(IAppProvider provider, string? instanceUrl, AuthMethod method, ICredentialSource credentials) =>
        new GatewayTransport(http, gatewayBase, notoAccessToken, provider, instanceUrl, method, credentials);
}
