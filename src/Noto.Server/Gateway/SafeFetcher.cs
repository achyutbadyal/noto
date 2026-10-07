using System.Net;
using System.Net.Sockets;
using Noto.Server.Config;
using Noto.Server.Middleware;

namespace Noto.Server.Gateway;

public sealed record SafeFetchOptions
{
    public int MaxRedirects { get; init; } = 5;
    public int MaxBytes { get; init; } = 1024 * 1024;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
    // Extra per-hop check (e.g. provider host allowlist); returning false aborts the redirect.
    public Func<Uri, bool>? AllowRedirect { get; init; }
}

public sealed record SafeResponse(int Status, IReadOnlyDictionary<string, string> Headers, byte[] Body, bool Truncated, Uri FinalUri);

// The only way the gateway touches the network. Every hop is validated: scheme, public address
// (DNS resolved and checked, then the connection is pinned to the checked IPs), redirect cap, size and time limits.
public sealed class SafeFetcher
{
    static readonly HttpRequestOptionsKey<IPAddress[]> PinnedIps = new("noto.pinned_ips");

    readonly IDnsResolver _dns;
    readonly HttpMessageInvoker _http;
    readonly IReadOnlySet<string> _allowPrivate;

    public SafeFetcher(IDnsResolver dns, HttpMessageHandler handler, ServerConfig config)
    {
        _dns = dns;
        _http = new HttpMessageInvoker(handler, disposeHandler: false);
        _allowPrivate = config.AllowPrivateHosts;
    }

    // Production handler: no auto redirect/proxy, and connects only to the IPs that were validated.
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = async (ctx, ct) =>
        {
            if (!ctx.InitialRequestMessage.Options.TryGetValue(PinnedIps, out var ips) || ips.Length == 0)
                throw new HttpRequestException("Connection without validated address");

            Exception? last = null;
            foreach (var ip in ips)
            {
                var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(ip, ctx.DnsEndPoint.Port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception e) { socket.Dispose(); last = e; }
            }
            throw new HttpRequestException("Connect failed", last);
        },
    };

    public async Task<SafeResponse> SendAsync(HttpRequestMessage request, SafeFetchOptions? options, CancellationToken ct)
    {
        options ??= new SafeFetchOptions();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Timeout);

        try
        {
            var current = request;
            for (var hop = 0; ; hop++)
            {
                var uri = current.RequestUri!;
                current.Options.Set(PinnedIps, await ValidateAsync(uri, timeout.Token));

                using var response = await _http.SendAsync(current, timeout.Token);
                var status = (int)response.StatusCode;

                if (status is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location)
                {
                    if (hop >= options.MaxRedirects) throw Bad("TOO_MANY_REDIRECTS", "Too many redirects");
                    var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    if (options.AllowRedirect is { } allow && !allow(next)) throw Forbidden("REDIRECT_NOT_ALLOWED", "Redirect leaves the allowed hosts");
                    current = Redirect(current, next, status);
                    continue;
                }

                var (body, truncated) = await ReadCappedAsync(response, options.MaxBytes, timeout.Token);
                var headers = response.Headers.Concat(response.Content.Headers)
                    .ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(",", h.Value));
                return new SafeResponse(status, headers, body, truncated, uri);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(502, "PROVIDER_TIMEOUT", "Upstream timed out");
        }
        catch (HttpRequestException)
        {
            throw new ApiException(502, "PROVIDER_UNREACHABLE", "Upstream unreachable");
        }
    }

    // Validates scheme/host and returns the addresses the connection must use.
    async Task<IPAddress[]> ValidateAsync(Uri uri, CancellationToken ct)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https")) throw Bad("INVALID_URL", "Only http(s) URLs are allowed");
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw Bad("INVALID_URL", "Credentials in URLs are not allowed");

        var trustedPrivate = _allowPrivate.Contains(uri.Host);
        // Plain http is only acceptable for hosts the operator explicitly allowlisted.
        if (uri.Scheme == "http" && !trustedPrivate) throw Forbidden("HTTP_NOT_ALLOWED", "https is required");

        var addresses = await _dns.ResolveAsync(uri.IdnHost, ct);
        if (addresses.Length == 0) throw new ApiException(502, "PROVIDER_UNREACHABLE", "Host did not resolve");
        if (!trustedPrivate && addresses.Any(a => !SsrfGuard.IsPublic(a)))
            throw Forbidden("PRIVATE_ADDRESS", "Host resolves to a non-public address");
        return addresses;
    }

    // 303 (and 301/302 on POST) become GET without a body; the Authorization header never follows to another host.
    static HttpRequestMessage Redirect(HttpRequestMessage from, Uri next, int status)
    {
        var method = status == 303 || (status is 301 or 302 && from.Method == HttpMethod.Post) ? HttpMethod.Get : from.Method;
        var clone = new HttpRequestMessage(method, next);
        foreach (var h in from.Headers)
            if (!(h.Key == "Authorization" && next.Host != from.RequestUri!.Host)) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (method != HttpMethod.Get && from.Content is not null) throw Bad("REDIRECT_NOT_ALLOWED", "Cannot redirect a request with a body");
        return clone;
    }

    static async Task<(byte[], bool)> ReadCappedAsync(HttpResponseMessage response, int max, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            var room = max - (int)buffer.Length;
            if (read > room) { buffer.Write(chunk, 0, room); return (buffer.ToArray(), true); }
            buffer.Write(chunk, 0, read);
        }
        return (buffer.ToArray(), false);
    }

    static ApiException Bad(string code, string title) => ApiException.BadRequest(code, title);
    static ApiException Forbidden(string code, string title) => ApiException.Forbidden(code, title);
}
