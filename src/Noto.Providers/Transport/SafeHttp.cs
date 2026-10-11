using System.Net;
using System.Net.Sockets;
using Noto.Core.Net;

namespace Noto.Providers.Transport;

// Handlers for the desktop's direct HTTP. Provider calls carry credentials and must not follow a redirect off
// the host that was asked; link previews carry none but must not be turned on the user's own network.
public static class SafeHttp
{
    // Provider traffic: redirects stay on the same origin; anything else comes back as the 3xx response.
    public static HttpMessageHandler ProviderHandler() =>
        new SameOriginRedirectHandler(new SocketsHttpHandler { AllowAutoRedirect = false });

    // Link previews: public addresses only, checked after DNS and pinned for the connection, so a name that
    // resolves to loopback, a LAN host or a metadata address is refused. Redirects are followed (each hop is
    // checked the same way) because there is no credential to leak.
    public static SocketsHttpHandler PreviewHandler() =>
        new()
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = async (ctx, ct) =>
            {
                var host = ctx.DnsEndPoint.Host;
                var addresses = IPAddress.TryParse(host, out var literal)
                    ? [literal]
                    : await Dns.GetHostAddressesAsync(host, ct);
                if (addresses.Length == 0 || addresses.Any(a => !PublicAddress.IsPublic(a)))
                    throw new HttpRequestException("Address is not public");
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(addresses, ctx.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

    public static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || uri.Host is "localhost" or "127.0.0.1" or "[::1]" or "::1";

    // Secrets go over https, except to this machine.
    public static bool IsSecureOrLoopback(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && IsLoopback(uri));
}

public sealed class SameOriginRedirectHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    const int MaxRedirects = 5;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        var response = await base.SendAsync(request, ct);
        for (var hop = 0; hop < MaxRedirects; hop++)
        {
            var status = (int)response.StatusCode;
            if (
                status is not (301 or 302 or 303 or 307 or 308)
                || response.Headers.Location is not { } location
            )
                return response;

            var from = request.RequestUri!;
            var next = location.IsAbsoluteUri ? location : new Uri(from, location);
            if (
                next.Scheme != from.Scheme
                || !string.Equals(next.Host, from.Host, StringComparison.OrdinalIgnoreCase)
                || next.Port != from.Port
            )
                return response; // the caller sees the redirect; credentials never leave the origin

            var keepMethod = status is 307 or 308;
            var method = keepMethod ? request.Method : HttpMethod.Get;
            var clone = new HttpRequestMessage(method, next);
            foreach (var h in request.Headers)
                clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
            if (keepMethod && request.Content is not null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync(ct);
                clone.Content = new ByteArrayContent(bytes);
                foreach (var h in request.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            response.Dispose();
            request = clone;
            response = await base.SendAsync(request, ct);
        }
        return response;
    }
}
