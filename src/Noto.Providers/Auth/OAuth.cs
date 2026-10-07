using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Noto.Providers.Auth;

// Embedded client ids/secrets are supplied by the app build; see docs/10 on why secrets are assumed extractable.
public sealed record OAuthClient(string ClientId, string? ClientSecret = null);

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

// RFC 8252 native flow: system browser + loopback redirect + PKCE.
public static class OAuthFlow
{
    public static Uri BuildAuthorizeUrl(
        AuthConfig config,
        OAuthClient client,
        string redirectUri,
        string state,
        string challenge
    )
    {
        var q = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = client.ClientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
        };
        q[config.ScopeParam] = string.Join(' ', config.Scopes);
        if (config.UsesPkce)
        {
            q["code_challenge"] = challenge;
            q["code_challenge_method"] = "S256";
        }
        return new Uri(
            config.AuthorizeUrl! + (config.AuthorizeUrl!.Contains('?') ? "&" : "?") + Form(q)
        );
    }

    public static Task<Credential> ExchangeCodeAsync(
        HttpClient http,
        AuthConfig config,
        OAuthClient client,
        string code,
        string redirectUri,
        string verifier,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = client.ClientId,
        };
        if (config.UsesPkce)
            form["code_verifier"] = verifier;
        if (client.ClientSecret is { } secret)
            form["client_secret"] = secret;
        return TokenRequestAsync(http, config, form, previousRefresh: null, now, ct);
    }

    public static Task<Credential> RefreshAsync(
        HttpClient http,
        AuthConfig config,
        OAuthClient client,
        string refreshToken,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = client.ClientId,
        };
        if (client.ClientSecret is { } secret)
            form["client_secret"] = secret;
        return TokenRequestAsync(http, config, form, previousRefresh: refreshToken, now, ct);
    }

    static async Task<Credential> TokenRequestAsync(
        HttpClient http,
        AuthConfig config,
        Dictionary<string, string> form,
        string? previousRefresh,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, config.TokenUrl)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new AuthRequiredException($"Token endpoint returned {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        // Slack v2 nests the user token under authed_user.
        if (
            !root.TryGetProperty("access_token", out _)
            && root.TryGetProperty("authed_user", out var user)
        )
            root = user;
        if (
            !root.TryGetProperty("access_token", out var access)
            || access.GetString() is not { Length: > 0 } token
        )
            throw new AuthRequiredException("Token endpoint returned no access token");

        var expiresIn =
            root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds)
                ? seconds
                : (int?)null;
        // Providers that omit a new refresh token expect the old one to stay valid.
        var refresh = root.TryGetProperty("refresh_token", out var r)
            ? r.GetString()
            : previousRefresh;
        return new Credential(token, refresh, expiresIn is { } s ? now.AddSeconds(s) : null);
    }

    static string Form(Dictionary<string, string> values) =>
        string.Join(
            '&',
            values.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}")
        );
}

// Listens on http://127.0.0.1:<random port>/callback for the single redirect, then stops.
public sealed class LoopbackReceiver : IDisposable
{
    readonly HttpListener _listener = new();

    public LoopbackReceiver()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        RedirectUri = $"http://127.0.0.1:{port}/callback";
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
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
            var valid = context.Request.Url?.AbsolutePath == "/callback";
            var ok =
                valid
                && query["state"] == expectedState
                && query["error"] is null
                && query["code"] is not null;
            await Respond(
                context,
                ok
                    ? "Connected. You can close this tab and return to Noto."
                    : "Authorization failed. Return to Noto and try again."
            );

            if (!valid)
                continue; // favicon and probes don't end the flow
            if (query["state"] != expectedState)
                throw new AuthRequiredException("OAuth state mismatch");
            if (query["error"] is { } error)
                throw new AuthRequiredException($"Authorization denied: {error}");
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
