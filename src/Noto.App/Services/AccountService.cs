using System.Text.Json;
using System.Text.Json.Serialization;
using Noto.Core.Interfaces;
using Noto.Core.Links;
using Noto.Providers.Auth;
using Noto.Sync;
using PlatformKeyring = Noto.Platform.Abstractions.IKeyring;

namespace Noto.App.Services;

public sealed record SignedInAccount(string Email, Uri Server);

// A provider that accepts a pasted personal token.
public sealed record TokenProvider(string Id, string Name);

// Sign-in to a sync server, and the connected-apps list. The refresh token lives in the OS keyring (the
// server rotates it on every refresh, so the stored copy is replaced each time). The access token stays in memory.
public sealed class AccountService(
    IServerAuth auth,
    PlatformKeyring keyring,
    IUiState ui,
    ServerDevice device,
    IUnitOfWork uow,
    ConnectionService connections,
    IReadOnlyList<TokenProvider> tokenProviders,
    TimeProvider time
)
{
    public const string KeyringService = "app.noto.server";
    const string SessionAccount = "session";
    const string ServerKey = "server.url";
    static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromMinutes(2);

    string? _accessToken;
    DateTimeOffset _expiresAt;

    public SignedInAccount? Account { get; private set; }
    public IReadOnlyList<TokenProvider> TokenProviders => tokenProviders;

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
        string providerId,
        string token,
        CancellationToken ct
    ) => connections.ConnectWithTokenAsync(providerId, token, AuthMethod.PersonalToken, ct: ct);

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
