using Noto.Core.Interfaces;
using Noto.Core.Links;
using Noto.Core.Time;
using Noto.Providers.Transport;

namespace Noto.Providers.Auth;

// Creates and removes connections. Secrets go to the keyring; only metadata reaches SQLite.
public sealed class ConnectionService(
    IUnitOfWork uow,
    CredentialStore credentials,
    ProviderRegistry registry,
    IProviderHttpFactory transports,
    IClock clock,
    HttpClient http,
    IReadOnlyDictionary<string, OAuthClient> oauthClients
)
{
    // Pastes a personal token, validates it with a cheap "who am I" call, then stores it.
    public async Task<AppConnection> ConnectWithTokenAsync(
        string providerId,
        string token,
        AuthMethod method,
        string? instanceUrl = null,
        string? username = null,
        CancellationToken ct = default
    )
    {
        var provider =
            registry.Get(providerId)
            ?? throw new ArgumentException("Unknown provider", nameof(providerId));
        if (!provider.SupportedAuthMethods.Contains(method))
            throw new ArgumentException($"{provider.DisplayName} does not support {method}");

        // Checked here, before any request, so a missing field is an error message rather than a crash.
        var site =
            provider.AcceptsSiteAddress && !string.IsNullOrWhiteSpace(instanceUrl)
                ? AbsoluteHttpUrl(instanceUrl)
                : null;
        if (provider.RequiresInstanceUrl && site is null)
            throw new ArgumentException(
                $"Enter the site address for {provider.DisplayName}, such as https://acme.atlassian.net"
            );
        if (provider.UsesUsername(method) && string.IsNullOrWhiteSpace(username))
            throw new ArgumentException(
                $"Enter the email address on your {provider.DisplayName} account"
            );

        return await FinishAsync(
            provider,
            new Credential(token.Trim(), Username: username?.Trim()),
            method,
            site,
            [],
            ct
        );
    }

    static string AbsoluteHttpUrl(string text)
    {
        if (
            !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
        )
            throw new ArgumentException("The site address must start with https://");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    // Opens `openBrowser` with the authorize URL and waits for the loopback redirect.
    public async Task<AppConnection> ConnectOAuthAsync(
        string providerId,
        Func<Uri, Task> openBrowser,
        string? instanceUrl = null,
        CancellationToken ct = default
    )
    {
        var provider =
            registry.Get(providerId)
            ?? throw new ArgumentException("Unknown provider", nameof(providerId));
        var config = provider.GetAuthConfig();
        if (!oauthClients.TryGetValue(providerId, out var client))
            throw new AuthRequiredException("No OAuth client is configured for this provider");

        var (verifier, challenge) = Pkce.Create();
        var state = Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)
        );
        using var receiver = new LoopbackReceiver();

        // Listen first so a fast redirect can never beat the listener.
        var pending = receiver.WaitForCodeAsync(state, ct);
        await openBrowser(
            OAuthFlow.BuildAuthorizeUrl(config, client, receiver.RedirectUri, state, challenge)
        );
        var code = await pending;
        var credential = await OAuthFlow.ExchangeCodeAsync(
            http,
            config,
            client,
            code,
            receiver.RedirectUri,
            verifier,
            clock.UtcNow,
            ct
        );
        return await FinishAsync(
            provider,
            credential,
            AuthMethod.OAuth2,
            instanceUrl,
            config.Scopes,
            ct
        );
    }

    async Task<AppConnection> FinishAsync(
        IAppProvider provider,
        Credential credential,
        AuthMethod method,
        string? instanceUrl,
        IReadOnlyList<string> scopes,
        CancellationToken ct
    )
    {
        var http = transports.Create(
            provider,
            instanceUrl,
            method,
            new StaticCredentialSource(credential)
        );
        var identity = await provider.ValidateAsync(http, ct);

        var connection = new AppConnection
        {
            Id = Guid.CreateVersion7(),
            ProviderId = provider.ProviderId,
            AuthMethod = method,
            DisplayLabel = identity.DisplayLabel,
            InstanceUrl = instanceUrl,
            Scopes = (identity.Scopes ?? scopes).ToArray(),
            ConnectedAt = clock.UtcNow,
            Status = ConnectionStatus.Active,
        };
        await credentials.SaveAsync(connection.Id, credential);
        await uow.RunAsync(async s =>
        {
            await s.Connections.UpsertAsync(connection);
            return 0;
        });
        return connection;
    }

    // Revokes at the provider when an API exists, then drops the keyring item and cached previews.
    public async Task DisconnectAsync(Guid connectionId, CancellationToken ct = default)
    {
        var connection = await uow.RunAsync(s => s.Connections.GetAsync(connectionId));
        if (connection is null)
            return;

        if (
            registry.Get(connection.ProviderId) is { } provider
            && await credentials.LoadAsync(connectionId) is { } credential
        )
        {
            try
            {
                await provider.RevokeAsync(
                    transports.Create(
                        provider,
                        connection.InstanceUrl,
                        connection.AuthMethod,
                        new StaticCredentialSource(credential)
                    ),
                    ct
                );
            }
            catch (Exception e) when (e is ProviderHttpException or HttpRequestException)
            { /* best effort */
            }
        }

        await credentials.DeleteAsync(connectionId);
        await uow.RunAsync(async s =>
        {
            await s.Previews.DeleteForConnectionAsync(connectionId);
            await s.Connections.DeleteAsync(connectionId);
            return 0;
        });
    }

    // Connection metadata for export/settings. Contains no secrets by construction.
    public async Task<string> ExportMetadataAsync()
    {
        var list = await uow.RunAsync(s => s.Connections.ListAsync());
        return System.Text.Json.JsonSerializer.Serialize(
            list.Select(c => new Dictionary<string, string?>
                {
                    ["provider"] = c.ProviderId,
                    ["label"] = c.DisplayLabel,
                    ["instance_url"] = c.InstanceUrl,
                    ["auth"] = c.AuthMethod.ToString(),
                })
                .ToList(),
            ExportJson.Default.ListDictionaryStringString
        );
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(List<Dictionary<string, string?>>))]
internal sealed partial class ExportJson : System.Text.Json.Serialization.JsonSerializerContext;
