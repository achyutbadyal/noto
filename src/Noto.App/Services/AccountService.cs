using System.Text.Json;
using System.Text.Json.Serialization;
using Noto.Core.Interfaces;
using Noto.Core.Links;
using Noto.Providers.Auth;
using Noto.Sync;
using PlatformKeyring = Noto.Platform.Abstractions.IKeyring;

namespace Noto.App.Services;

public sealed record SignedInAccount(string Email, Uri Server);

public enum SiteField
{
    None,
    Optional,
    Required,
}

// One way to connect by pasting a secret: a provider with a specific auth method (Jira with an API key, GitHub with a token).
public sealed record TokenOption(
    string ProviderId,
    string ProviderName,
    AuthMethod Method,
    SiteField Site,
    bool NeedsEmail
)
{
    public string MethodLabel => Method == AuthMethod.ApiKey ? "API key" : "personal access token";

    public string Label => $"{ProviderName} · {MethodLabel}";
}

// A provider that can sign in through the browser. Configured is false when this build has no OAuth app for it.
// ShowSite: the provider asks which site to connect. A blank site means the account's first one.
public sealed record OAuthOption(
    string ProviderId,
    string ProviderName,
    bool Configured,
    string SetupHint,
    bool ShowSite = false,
    // The provider's ID on the server, when it differs from ProviderId (Jira and Confluence share "atlassian").
    string? ServerProviderId = null
);

// Sign-in to a sync server, and the connected-apps list. The refresh token lives in the OS keyring (the
// server rotates it on every refresh, so the stored copy is replaced each time). The access token stays in memory.
public sealed class AccountService(
    IServerAuth auth,
    PlatformKeyring keyring,
    IUiState ui,
    ServerDevice device,
    IUnitOfWork uow,
    ConnectionService connections,
    IReadOnlyList<TokenOption> tokenOptions,
    TimeProvider time,
    // Providers this build can sign in with (the candidates); the server decides which are configured.
    IReadOnlyList<OAuthOption>? oauthCandidates = null,
    Func<Uri, Task>? openBrowser = null,
    Func<CancellationToken, Task<IReadOnlyList<GatewayProvider>>>? serverProviders = null
)
{
    public const string KeyringService = "app.noto.server";
    const string SessionAccount = "session";
    const string ServerKey = "server.url";
    static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromMinutes(2);

    string? _accessToken;
    DateTimeOffset _expiresAt;

    public SignedInAccount? Account { get; private set; }
    public IReadOnlyList<TokenOption> TokenOptions => tokenOptions;

    // The browser sign-in options for Settings. Configured means the signed-in server can run that provider.
    // Without a session, or when the server can't be reached, nothing is configured.
    public async Task<IReadOnlyList<OAuthOption>> LoadOAuthOptionsAsync(CancellationToken ct)
    {
        var candidates = oauthCandidates ?? [];
        if (Account is null || serverProviders is null)
            return candidates.Select(o => o with { Configured = false }).ToList();
        var available = (await serverProviders(ct)).Select(p => p.Id).ToHashSet();
        return candidates
            .Select(o =>
                o with
                {
                    Configured = available.Contains(o.ServerProviderId ?? o.ProviderId),
                }
            )
            .ToList();
    }

    // A valid server session for providers that run through the server. Throws when signed out.
    public async Task<(Uri Server, string Token)> SessionAsync(CancellationToken ct)
    {
        var token = await AccessTokenAsync(ct);
        return Account is { } account && token is not null
            ? (account.Server, token)
            : throw new AuthRequiredException(
                "Sign in to your Noto server first (Settings, Account and sync)."
            );
    }

    public string? ServerText => ui.Get(ServerKey);

    // Accepts http(s) URLs only. Clears the stored value when the text is blank.
    public void SetServer(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ui.Set(ServerKey, null);
            return;
        }
        if (
            !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
        )
            throw new ArgumentException("Enter a full http:// or https:// address");
        ui.Set(ServerKey, uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    public async Task SignUpAsync(
        string email,
        string password,
        string? inviteCode,
        CancellationToken ct
    )
    {
        var server = RequireServer();
        var s = await auth.SignUpAsync(server, email.Trim(), password, inviteCode, device, ct);
        await RememberAsync(server, email.Trim(), s);
    }

    public async Task SignInAsync(string email, string password, CancellationToken ct)
    {
        var server = RequireServer();
        var s = await auth.SignInAsync(server, email.Trim(), password, device, ct);
        await RememberAsync(server, email.Trim(), s);
    }

    // Revokes the session on the server when it can, then forgets it locally either way.
    public async Task SignOutAsync(CancellationToken ct)
    {
        if (Account is null)
            return;
        try
        {
            var token = await AccessTokenAsync(ct);
            if (token is not null)
                await auth.SignOutAsync(Account.Server, token, ct);
        }
        catch (ServerAuthException) { }
        await ForgetAsync();
    }

    // Startup: reloads the saved session. A revoked session is dropped; an unreachable server keeps the
    // user signed in, and the token is fetched again on the next use.
    public async Task RestoreAsync(CancellationToken ct)
    {
        var stored = await LoadAsync();
        if (stored is null)
            return;
        Account = new SignedInAccount(stored.Email, new Uri(stored.Server));
        try
        {
            var s = await auth.RefreshAsync(Account.Server, stored.RefreshToken, ct);
            await RememberAsync(Account.Server, stored.Email, s);
        }
        catch (ServerAuthException e) when (e.Code is "INVALID_REFRESH_TOKEN" or "DEVICE_REVOKED")
        {
            await ForgetAsync();
        }
        catch (ServerAuthException) { }
    }

    // A valid access token for the signed-in session, refreshed when close to expiry. Null when signed out.
    public async Task<string?> AccessTokenAsync(CancellationToken ct)
    {
        if (Account is null)
            return null;
        if (_accessToken is not null && _expiresAt - time.GetUtcNow() > RefreshBeforeExpiry)
            return _accessToken;
        var stored = await LoadAsync() ?? throw new InvalidOperationException("Session missing");
        var s = await auth.RefreshAsync(Account.Server, stored.RefreshToken, ct);
        await RememberAsync(Account.Server, stored.Email, s);
        return _accessToken;
    }

    public Task<IReadOnlyList<AppConnection>> ListConnectionsAsync() =>
        uow.RunAsync(s => s.Connections.ListAsync());

    public Task<AppConnection> ConnectTokenAsync(
        TokenOption option,
        string token,
        string? site,
        string? email,
        CancellationToken ct
    ) =>
        connections.ConnectWithTokenAsync(option.ProviderId, token, option.Method, site, email, ct);

    // Opens the provider's sign-in page in the browser and waits for the redirect back to this device.
    public Task<AppConnection> ConnectOAuthAsync(
        string providerId,
        string? site,
        CancellationToken ct
    ) =>
        connections.ConnectOAuthAsync(
            providerId,
            openBrowser
                ?? throw new InvalidOperationException("Browser sign-in isn't available here"),
            site,
            ct
        );

    public Task DisconnectAsync(Guid connectionId, CancellationToken ct) =>
        connections.DisconnectAsync(connectionId, ct);

    Uri RequireServer() =>
        Uri.TryCreate(ServerText, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException("Set the server address first");

    async Task RememberAsync(Uri server, string email, ServerSession s)
    {
        _accessToken = s.AccessToken;
        _expiresAt = s.ExpiresAt;
        Account = new SignedInAccount(email, server);
        await keyring.SetAsync(
            KeyringService,
            SessionAccount,
            JsonSerializer.Serialize(
                new StoredSession(server.ToString(), email, s.RefreshToken),
                SessionJson.Default.StoredSession
            )
        );
    }

    async Task ForgetAsync()
    {
        _accessToken = null;
        Account = null;
        await keyring.DeleteAsync(KeyringService, SessionAccount);
    }

    async Task<StoredSession?> LoadAsync()
    {
        var json = await keyring.GetAsync(KeyringService, SessionAccount);
        return json is null
            ? null
            : JsonSerializer.Deserialize(json, SessionJson.Default.StoredSession);
    }
}

public sealed record StoredSession(string Server, string Email, string RefreshToken);

[JsonSerializable(typeof(StoredSession))]
sealed partial class SessionJson : JsonSerializerContext;
