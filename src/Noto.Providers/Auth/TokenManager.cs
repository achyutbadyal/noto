using System.Collections.Concurrent;
using Noto.Core.Interfaces;
using Noto.Core.Links;
using Noto.Core.Time;

namespace Noto.Providers.Auth;

// Hands out valid credentials, refreshing under a single-flight lock per connection (docs/10 › OAuth).
public sealed class TokenManager(
    IUnitOfWork uow,
    CredentialStore credentials,
    ProviderRegistry registry,
    IClock clock,
    IOAuthGateway gateway
)
{
    static readonly TimeSpan Skew = TimeSpan.FromMinutes(2);
    readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public ICredentialSource For(AppConnection connection) => new Source(this, connection.Id);

    public async Task<Credential> GetValidAsync(Guid connectionId, CancellationToken ct)
    {
        var credential =
            await credentials.LoadAsync(connectionId)
            ?? throw new AuthRequiredException("No credential stored");
        if (!IsExpiring(credential))
            return credential;

        var gate = _locks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Another caller may have refreshed while we waited.
            credential =
                await credentials.LoadAsync(connectionId)
                ?? throw new AuthRequiredException("No credential stored");
            if (!IsExpiring(credential))
                return credential;

            if (credential.RefreshToken is null)
            {
                await SetStatusAsync(connectionId, ConnectionStatus.Expired);
                throw new AuthRequiredException("Token expired and cannot be refreshed");
            }
            return await RefreshAsync(connectionId, credential.RefreshToken, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    async Task<Credential> RefreshAsync(
        Guid connectionId,
        string refreshToken,
        CancellationToken ct
    )
    {
        var connection =
            await uow.RunAsync(s => s.Connections.GetAsync(connectionId))
            ?? throw new AuthRequiredException("Connection not found");
        try
        {
            var provider =
                registry.Get(connection.ProviderId)
                ?? throw new AuthRequiredException("Unknown provider");
            // The server holds the provider secret, so refresh goes through it.
            var fresh = await gateway.RefreshAsync(GatewayIds.For(provider), refreshToken, ct);
            // Providers that don't rotate the refresh token omit it; the one we have stays valid.
            fresh = fresh with
            {
                RefreshToken = fresh.RefreshToken ?? refreshToken,
            };
            await credentials.SaveAsync(connectionId, fresh);
            return fresh;
        }
        catch (ProviderRejectedException)
        {
            // Only the provider refusing the grant means the user must reconnect. An outage or an expired
            // server session leaves the connection alone: the provider token may still be valid.
            await SetStatusAsync(connectionId, ConnectionStatus.RefreshFailed);
            throw new AuthRequiredException("Token refresh failed");
        }
    }

    bool IsExpiring(Credential c) => c.ExpiresAt is { } at && at - Skew <= clock.UtcNow;

    Task SetStatusAsync(Guid connectionId, ConnectionStatus status) =>
        uow.RunAsync(async s =>
        {
            if (await s.Connections.GetAsync(connectionId) is { } c)
            {
                c.Status = status;
                await s.Connections.UpsertAsync(c);
            }
            return 0;
        });

    sealed class Source(TokenManager owner, Guid connectionId) : ICredentialSource
    {
        public async Task<Credential?> GetAsync(CancellationToken ct) =>
            await owner.GetValidAsync(connectionId, ct);
    }
}
