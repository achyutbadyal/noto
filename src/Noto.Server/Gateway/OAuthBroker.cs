using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Noto.Server.Config;
using Noto.Server.Middleware;

namespace Noto.Server.Gateway;

public sealed record OAuthStartRequest(
    [property: JsonPropertyName("instance_url")] string? InstanceUrl,
    [property: JsonPropertyName("code_challenge")] string? CodeChallenge,
    // Where the browser goes after the exchange: a loopback address the desktop app is listening on.
    [property: JsonPropertyName("return_to")] string? ReturnTo = null
);

// Result of the browser redirect. Exactly one of OneTimeCode and ErrorCode is set. ReturnTo is set when the
// flow started from a desktop app, which receives the result on its loopback listener instead of a popup.
public sealed record OAuthCompletion(
    string? ReturnTo,
    string State,
    string? OneTimeCode,
    string? ErrorCode
);

public sealed record OAuthProviderInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name
);

public sealed record OAuthStartResponse(
    [property: JsonPropertyName("authorize_url")] string AuthorizeUrl,
    [property: JsonPropertyName("state")] string State
);

public sealed record RedeemRequest(
    [property: JsonPropertyName("one_time_code")] string? OneTimeCode,
    [property: JsonPropertyName("code_verifier")] string? CodeVerifier
);

public sealed record RefreshRequest(
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("instance_url")] string? InstanceUrl
);

public sealed record ProviderTokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("scopes")] string? Scopes,
    [property: JsonPropertyName("display_label")] string DisplayLabel
);

// Brokers provider OAuth for the browser client. The server holds the provider client secret and does the
// code exchange; tokens exist only in a 60-second in-memory slot until the client redeems them.
public sealed class OAuthBroker(
    IConfiguration settings,
    ServerConfig config,
    SafeFetcher fetcher,
    IMemoryCache cache,
    TimeProvider time
)
{
    static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);
    static readonly TimeSpan CodeTtl = TimeSpan.FromSeconds(60);

    sealed record PendingAuth(
        Guid UserId,
        string ProviderId,
        string? InstanceUrl,
        string ServerVerifier,
        string? ClientChallenge,
        string? ReturnTo,
        DateTimeOffset ExpiresAt
    );

    sealed record PendingCode(
        Guid UserId,
        string ProviderId,
        ProviderTokens Tokens,
        string? ClientChallenge,
        DateTimeOffset ExpiresAt
    );

    public OAuthStartResponse Start(Guid userId, string providerId, OAuthStartRequest req)
    {
        var (provider, oauth, clientId, _) = Configured(providerId);
        var tokenBase = InstanceBase(provider, req.InstanceUrl);
        var returnTo = LoopbackReturn.Validate(req.ReturnTo);

        var state = Random();
        var verifier = Random();
        cache.Set(
            "oauth:" + state,
            new PendingAuth(
                userId,
                providerId,
                req.InstanceUrl,
                verifier,
                req.CodeChallenge,
                returnTo,
                time.GetUtcNow() + StateTtl
            ),
            StateTtl
        );

        var query = new List<(string, string)>
        {
            ("client_id", clientId),
            ("redirect_uri", RedirectUri(providerId)),
            ("response_type", "code"),
            ("state", state),
        };
        if (oauth.Scopes.Length > 0)
            query.Add(
                (provider.Id == "slack" ? "user_scope" : "scope", string.Join(" ", oauth.Scopes))
            );
        if (oauth.Pkce)
        {
            query.Add(("code_challenge", Challenge(verifier)));
            query.Add(("code_challenge_method", "S256"));
        }
        foreach (var (k, v) in oauth.ExtraAuthorizeParams ?? new Dictionary<string, string>())
            query.Add((k, v));

        var authorize = tokenBase is null
            ? oauth.AuthorizeUrl
            : $"{tokenBase}{new Uri(oauth.AuthorizeUrl).AbsolutePath}";
        return new OAuthStartResponse(
            $"{authorize}?{string.Join("&", query.Select(q => $"{Uri.EscapeDataString(q.Item1)}={Uri.EscapeDataString(q.Item2)}"))}",
            state
        );
    }

    // Browser redirect target: exchanges the code and returns a one-time code for the opener window.
    public async Task<OAuthCompletion> CompleteAsync(
        string providerId,
        string? code,
        string? state,
        CancellationToken ct
    )
    {
        if (
            string.IsNullOrEmpty(state)
            || !cache.TryGetValue("oauth:" + state, out PendingAuth? pending)
            || pending is null
        )
            throw ApiException.BadRequest(
                "INVALID_STATE",
                "Unknown or expired authorization state"
            );
        cache.Remove("oauth:" + state); // single use, even if the checks below fail
        if (pending.ProviderId != providerId || pending.ExpiresAt <= time.GetUtcNow())
            throw ApiException.BadRequest(
                "INVALID_STATE",
                "Unknown or expired authorization state"
            );

        // A desktop flow hears about failures on its own redirect; a browser popup shows the error page.
        try
        {
            var oneTime = await ExchangeAndStoreAsync(providerId, code, pending, ct);
            return new OAuthCompletion(pending.ReturnTo, state, oneTime, null);
        }
        catch (Exception e)
            when (pending.ReturnTo is not null && e is not OperationCanceledException)
        {
            // Anything that stops the exchange must reach the app, or it waits for the full sign-in timeout.
            var errorCode = e is ApiException api ? api.Code : "EXCHANGE_FAILED";
            return new OAuthCompletion(pending.ReturnTo, state, null, errorCode);
        }
    }

    async Task<string> ExchangeAndStoreAsync(
        string providerId,
        string? code,
        PendingAuth pending,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(code))
            throw ApiException.BadRequest("AUTHORIZATION_DENIED", "Authorization was not granted");

        var (provider, oauth, clientId, secret) = Configured(providerId);
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri(providerId),
        };
        if (oauth.Pkce)
            fields["code_verifier"] = pending.ServerVerifier;

        var tokens = await ExchangeAsync(
            provider,
            oauth,
            clientId,
            secret,
            pending.InstanceUrl,
            fields,
            ct
        );
        var oneTime = Random();
        cache.Set(
            "code:" + oneTime,
            new PendingCode(
                pending.UserId,
                providerId,
                tokens,
                pending.ClientChallenge,
                time.GetUtcNow() + CodeTtl
            ),
            CodeTtl
        );
        return oneTime;
    }

    // The client ID and secret from .env, or null when either is missing: the provider is then not enabled.
    (string ClientId, string Secret)? Credentials(ProviderDef provider)
    {
        var clientId = settings[$"{provider.EnvPrefix}_CLIENT_ID"];
        var secret = settings[$"{provider.EnvPrefix}_CLIENT_SECRET"];
        return string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(secret)
            ? null
            : (clientId, secret);
    }

    // Providers this server can sign people in with: the catalog entry plus both secrets from .env.
    public IReadOnlyList<OAuthProviderInfo> Available() =>
        ProviderCatalog
            .All.Where(p => p.OAuth is not null)
            .Where(p => Credentials(p) is not null)
            .Select(p => new OAuthProviderInfo(p.Id, p.Name))
            .ToList();

    public ProviderTokens Redeem(Guid userId, string providerId, RedeemRequest req)
    {
        if (
            string.IsNullOrEmpty(req.OneTimeCode)
            || !cache.TryGetValue("code:" + req.OneTimeCode, out PendingCode? pending)
            || pending is null
        )
            throw ApiException.BadRequest("INVALID_CODE", "Unknown or expired code");
        cache.Remove("code:" + req.OneTimeCode); // single use, even when the checks below fail
        if (pending.ExpiresAt <= time.GetUtcNow())
            throw ApiException.BadRequest("INVALID_CODE", "Unknown or expired code");

        if (pending.UserId != userId || pending.ProviderId != providerId)
            throw ApiException.Forbidden("INVALID_CODE", "Code was issued to another user");
        if (
            pending.ClientChallenge is { } challenge
            && (req.CodeVerifier is null || Challenge(req.CodeVerifier) != challenge)
        )
            throw ApiException.Forbidden(
                "PKCE_MISMATCH",
                "code_verifier does not match code_challenge"
            );
        return pending.Tokens;
    }

    public async Task<ProviderTokens> RefreshAsync(
        string providerId,
        RefreshRequest req,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(req.RefreshToken))
            throw ApiException.BadRequest("VALIDATION_ERROR", "refresh_token is required");
        var (provider, oauth, clientId, secret) = Configured(providerId);
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = req.RefreshToken,
        };
        return await ExchangeAsync(provider, oauth, clientId, secret, req.InstanceUrl, fields, ct);
    }

    async Task<ProviderTokens> ExchangeAsync(
        ProviderDef provider,
        OAuthDef oauth,
        string clientId,
        string secret,
        string? instanceUrl,
        Dictionary<string, string> fields,
        CancellationToken ct
    )
    {
        var instance = InstanceBase(provider, instanceUrl);
        var tokenUrl = instance is null
            ? oauth.TokenUrl
            : $"{instance}{new Uri(oauth.TokenUrl).AbsolutePath}";

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (oauth.BasicAuthToken)
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{secret}"))
            );
        else
        {
            fields["client_id"] = clientId;
            fields["client_secret"] = secret;
        }

        request.Content = oauth.JsonTokenRequest
            ? new StringContent(JsonSerializer.Serialize(fields), Encoding.UTF8, "application/json")
            : new FormUrlEncodedContent(fields);

        var response = await fetcher.SendAsync(
            request,
            new SafeFetchOptions { Timeout = TimeSpan.FromSeconds(10) },
            ct
        );
        JsonObject? json;
        try
        {
            json = JsonNode.Parse(response.Body) as JsonObject;
        }
        catch (JsonException)
        {
            json = null;
        }

        // Providers report failures in the body (Slack: ok=false) as well as in the status code.
        if (
            response.Status is < 200 or >= 300
            || json is null
            || json["access_token"] is null && json["authed_user"]?["access_token"] is null
        )
            throw new ApiException(
                502,
                "PROVIDER_AUTH_FAILED",
                "Provider rejected the authorization"
            );

        var access =
            json["access_token"]?.GetValue<string>()
            ?? json["authed_user"]!["access_token"]!.GetValue<string>();
        var expiresIn = json["expires_in"]?.GetValue<double>();
        return new ProviderTokens(
            access,
            json["refresh_token"]?.GetValue<string>(),
            expiresIn is { } s ? time.GetUtcNow().AddSeconds(s) : null,
            json["scope"]?.GetValue<string>(),
            instance is null ? provider.Name : $"{provider.Name} ({new Uri(instance).Host})"
        );
    }

    (ProviderDef Provider, OAuthDef OAuth, string ClientId, string Secret) Configured(
        string providerId
    )
    {
        var provider =
            ProviderCatalog.Find(providerId)
            ?? throw ApiException.NotFound("UNKNOWN_PROVIDER", "Unknown provider");
        if (provider.OAuth is null || Credentials(provider) is not { } credentials)
            throw ApiException.NotFound(
                "PROVIDER_NOT_CONFIGURED",
                "This provider is not enabled on this server"
            );
        return (provider, provider.OAuth, credentials.ClientId, credentials.Secret);
    }

    // Self-hosted providers (GitLab, Atlassian DC) carry their own instance host in the OAuth URLs.
    static string? InstanceBase(ProviderDef provider, string? instanceUrl)
    {
        if (string.IsNullOrWhiteSpace(instanceUrl))
            return null;
        if (!provider.AllowInstanceUrl)
            throw ApiException.BadRequest(
                "INSTANCE_URL_NOT_SUPPORTED",
                "This provider has no instance_url"
            );
        if (
            !Uri.TryCreate(instanceUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || !string.IsNullOrEmpty(uri.UserInfo)
        )
            throw ApiException.BadRequest(
                "INVALID_INSTANCE_URL",
                "instance_url must be an absolute https URL"
            );
        return uri.GetLeftPart(UriPartial.Authority);
    }

    string RedirectUri(string providerId) =>
        new Uri(config.PublicUrl, $"/v1/gateway/oauth/{providerId}/callback").ToString();

    static string Random() =>
        Convert
            .ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static string Challenge(string verifier) =>
        Convert
            .ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
