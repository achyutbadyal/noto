using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Noto.Providers.Auth;

public static class Pkce
{
    public static (string Verifier, string Challenge) Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class OAuthFlow
{
    // Gives up after this long, so an abandoned browser tab doesn't leave the sign-in button stuck.
    public static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);
}

// Listens on http://127.0.0.1:<port>/callback for the one redirect from the Noto server, then stops. The port is
// picked at random: the server accepts any loopback port, and no provider ever sees this address.
public sealed class LoopbackReceiver : IDisposable
{
    readonly HttpListener _listener = new();

    // A null port picks a free one. Tests pass a port they know is free.
    public LoopbackReceiver(int? port = null)
    {
        var chosen = port ?? FreePort();
        RedirectUri = $"http://127.0.0.1:{chosen}/callback";
        _listener.Prefixes.Add($"http://127.0.0.1:{chosen}/");
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            _listener.Close();
            throw new AuthRequiredException(
                $"Port {chosen} is in use by another app. Close it and try again."
            );
        }
    }

    static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public string RedirectUri { get; }

    public async Task<string> WaitForCodeAsync(string expectedState, CancellationToken ct)
    {
        using var reg = ct.Register(_listener.Stop);
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            var query = context.Request.QueryString;
            // Only the callback with this sign-in's state counts. Anything else (favicon, probes, a stale tab)
            // gets a page and is ignored, so it can't end the sign-in.
            var ours =
                context.Request.Url?.AbsolutePath == "/callback" && query["state"] == expectedState;
            var ok = ours && query["error"] is null && query["code"] is not null;
            await Respond(
                context,
                ok
                    ? "Connected. You can close this tab and return to Noto."
                    : "Authorization failed. Return to Noto and try again."
            );

            if (!ours)
                continue;
            if (query["error"] is { } error)
                throw new AuthRequiredException($"Authorization denied: {error}");
            if (query["code"] is null)
                throw new AuthRequiredException("The sign-in response had no code");
            return query["code"]!;
        }
    }

    static async Task Respond(HttpListenerContext context, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"<html><body><p>{WebUtility.HtmlEncode(message)}</p></body></html>"
        );
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    public void Dispose() => _listener.Close();
}
