namespace Noto.Providers.Auth;

// Provider OAuth runs on the Noto server, which holds the provider secrets and does the code exchange. The
// desktop app only sends the user to the provider and then asks the server for tokens, with its own session.
public interface IOAuthGateway
{
    // Providers this server has client credentials for. Requires a signed-in session.
    Task<IReadOnlyList<GatewayProvider>> ListAsync(CancellationToken ct);

    // Returns the provider's authorize URL. The server sends the browser to returnTo with a one-time code.
    Task<GatewayStart> StartAsync(
        string providerId,
        string codeChallenge,
        string returnTo,
        CancellationToken ct
    );

    // Trades the one-time code, with the PKCE verifier, for the provider's tokens.
    Task<Credential> RedeemAsync(
        string providerId,
        string oneTimeCode,
        string codeVerifier,
        CancellationToken ct
    );

    Task<Credential> RefreshAsync(string providerId, string refreshToken, CancellationToken ct);
}

// The provider refused the grant, for example a revoked refresh token. Only this means the user must reconnect.
public sealed class ProviderRejectedException(string message) : AuthRequiredException(message);

public sealed record GatewayProvider(string Id, string Name);

public static class GatewayIds
{
    // The ID this provider has on the Noto server.
    public static string For(IAppProvider provider) =>
        provider.GetAuthConfig().ServerProviderId ?? provider.ProviderId;
}

public sealed record GatewayStart(string AuthorizeUrl, string State);
