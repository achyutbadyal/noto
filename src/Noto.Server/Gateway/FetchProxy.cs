using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Noto.Server.Middleware;

namespace Noto.Server.Gateway;

public sealed record FetchRequest(
    [property: JsonPropertyName("provider_id")] string? ProviderId,
    [property: JsonPropertyName("instance_url")] string? InstanceUrl,
    [property: JsonPropertyName("request")] FetchInner? Request
);

public sealed record FetchInner(
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("query")] Dictionary<string, string>? Query,
    [property: JsonPropertyName("body")] JsonNode? Body
);

public sealed record FetchResponse(
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers,
    [property: JsonPropertyName("body")] string Body
);

// Stateless pass-through: the provider token lives only in this call's memory. Nothing request- or
// response-related is logged; metrics carry provider id, status and latency only.
public sealed class FetchProxy(ProviderAllowlist allowlist, SafeFetcher fetcher)
{
    static readonly Meter Meter = new("Noto.Gateway");
    static readonly Counter<long> Requests = Meter.CreateCounter<long>("gateway.fetch.requests");
    static readonly Histogram<double> Latency = Meter.CreateHistogram<double>(
        "gateway.fetch.duration_ms"
    );

    static readonly HashSet<string> ExposedHeaders =
    [
        "content-type",
        "etag",
        "link",
        "retry-after",
        "x-ratelimit-limit",
        "x-ratelimit-remaining",
        "x-ratelimit-reset",
    ];

    public async Task<FetchResponse> FetchAsync(
        FetchRequest req,
        string? providerAuthorization,
        CancellationToken ct
    )
    {
        var provider =
            ProviderCatalog.Find(req.ProviderId ?? "")
            ?? throw ApiException.BadRequest("UNKNOWN_PROVIDER", "Unknown provider");
        var inner =
            req.Request ?? throw ApiException.BadRequest("VALIDATION_ERROR", "request is required");
        var bodyText = inner.Body?.ToJsonString();

        var allowed = allowlist.Resolve(
            provider,
            req.InstanceUrl,
            inner.Method ?? "GET",
            inner.Path ?? "",
            inner.Query,
            bodyText
        );

        using var message = new HttpRequestMessage(
            allowed.IsGraphQl ? HttpMethod.Post : HttpMethod.Get,
            allowed.Target
        );
        message.Headers.TryAddWithoutValidation("Accept", "application/json");
        message.Headers.TryAddWithoutValidation("User-Agent", "Noto-Gateway/1.0");
        if (!string.IsNullOrEmpty(providerAuthorization))
            message.Headers.TryAddWithoutValidation("Authorization", providerAuthorization);
        if (allowed.IsGraphQl)
            message.Content = new StringContent(bodyText!, Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        var status = 502;
        try
        {
            var response = await fetcher.SendAsync(
                message,
                new SafeFetchOptions
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    AllowRedirect = uri =>
                        allowlist.IsAllowedRedirect(provider, allowed.Target, uri),
                },
                ct
            );
            status = response.Status;

            if (response.Truncated)
                throw new ApiException(502, "RESPONSE_TOO_LARGE", "Provider response too large");
            var headers = response
                .Headers.Where(h => ExposedHeaders.Contains(h.Key))
                .ToDictionary(h => h.Key, h => h.Value);
            return new FetchResponse(
                response.Status,
                headers,
                Encoding.UTF8.GetString(response.Body)
            );
        }
        finally
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                new("provider_id", provider.Id),
                new("status", status),
            };
            Requests.Add(1, tags);
            Latency.Record(sw.Elapsed.TotalMilliseconds, tags);
        }
    }
}
