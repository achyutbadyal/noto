using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Noto.Providers;
using Noto.Providers.Auth;
using Noto.Sync;

namespace Noto.App.Services;

// Talks to the Noto server's OAuth broker (docs/10). Requests carry the signed-in session, so only a user who
// is signed in to their server can connect a provider through it.
public sealed class ServerOAuthGateway(
    HttpClient http,
    Func<CancellationToken, Task<(Uri Server, string Token)>> session
) : IOAuthGateway
{
    public async Task<IReadOnlyList<GatewayProvider>> ListAsync(CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get, "v1/gateway/oauth", null, ct);
        return doc
            .RootElement.EnumerateArray()
            .Select(p => new GatewayProvider(
                p.GetProperty("id").GetString()!,
                p.GetProperty("name").GetString()!
            ))
            .ToList();
    }

    public async Task<GatewayStart> StartAsync(
        string providerId,
        string codeChallenge,
        string returnTo,
        CancellationToken ct
    )
    {
        using var doc = await SendAsync(
            HttpMethod.Post,
            $"v1/gateway/oauth/{providerId}/start",
            new { code_challenge = codeChallenge, return_to = returnTo },
            ct
        );
        return new GatewayStart(
            doc.RootElement.GetProperty("authorize_url").GetString()!,
            doc.RootElement.GetProperty("state").GetString()!
        );
    }

    public async Task<Credential> RedeemAsync(
        string providerId,
        string oneTimeCode,
        string codeVerifier,
        CancellationToken ct
    )
    {
        using var doc = await SendAsync(
            HttpMethod.Post,
            $"v1/gateway/oauth/{providerId}/redeem",
            new { one_time_code = oneTimeCode, code_verifier = codeVerifier },
            ct
        );
        return ToCredential(doc.RootElement);
    }

    public async Task<Credential> RefreshAsync(
        string providerId,
        string refreshToken,
        CancellationToken ct
    )
    {
        using var doc = await SendAsync(
            HttpMethod.Post,
            $"v1/gateway/oauth/{providerId}/refresh",
            new { refresh_token = refreshToken },
            ct
        );
        return ToCredential(doc.RootElement);
    }

    // The server's ProviderTokens shape: access_token, refresh_token, expires_at, scopes, display_label.
    static Credential ToCredential(JsonElement tokens) =>
        new(
            tokens.GetProperty("access_token").GetString()
                ?? throw new AuthRequiredException("The server returned no access token"),
            tokens.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            tokens.TryGetProperty("expires_at", out var expires)
            && expires.ValueKind == JsonValueKind.String
                ? DateTimeOffset.Parse(expires.GetString()!)
                : null
        );

    async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct
    )
    {
        var (server, token) = await session(ct);
        using var request = new HttpRequestMessage(method, new Uri(server, path))
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            token
        );
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
            return JsonDocument.Parse(text);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new AuthRequiredException(
                "Your Noto server session has ended. Sign in to the server again."
            );
        var problem = ProblemDetails.Read(text);
        if (problem.Code == "PROVIDER_AUTH_FAILED")
            throw new ProviderRejectedException(
                problem.Title ?? "The provider rejected the authorization"
            );
        throw new AuthRequiredException(
            problem.Title ?? $"The Noto server returned {(int)response.StatusCode}"
        );
    }
}
