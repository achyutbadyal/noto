using Microsoft.Extensions.Logging.Abstractions;
using Noto.App.Services;
using Noto.Core.Links;
using Noto.Providers;
using Noto.Providers.Auth;
using Noto.Providers.Preview;
using Noto.Providers.Transport;

namespace Noto.App.Tests;

// Previews over the fixture's own database with no provider registered. Every path a screen takes is
// cache and clock only, so the transport is never reached — which is the property that lets a row draw a
// link without waiting on the network (docs/10 › Detection pipeline). The stubs throw rather than
// returning something plausible, so a test that starts reaching for the network says so.
public static class PreviewCache
{
    public static LinkPreviews Build(AppFixture app)
    {
        var registry = new ProviderRegistry([]);
        return new LinkPreviews(
            app.Db,
            new PreviewService(
                app.Db,
                registry,
                new UnreachableTransport(),
                new TokenManager(
                    app.Db,
                    new CredentialStore(new Noto.Providers.Auth.InMemoryKeyring()),
                    registry,
                    app.Clock,
                    new UnreachableGateway()
                ),
                app.Clock,
                new UnreachableDelay(),
                NullLogger<PreviewService>.Instance
            ),
            registry,
            app.Clock
        );
    }

    // Seeds the cache directly and fresh, so nothing is due and no fetch is fired behind the test's back.
    // `viewed` is the state the user last looked at; a different `state` is a change waiting to be seen.
    public static async Task PutAsync(
        AppFixture app,
        string url,
        string state,
        string viewed,
        PreviewStatus status = PreviewStatus.Loaded
    ) =>
        await app.Db.RunAsync(async s =>
        {
            await s.Previews.PutAsync(
                new LinkPreview
                {
                    Url = url,
                    ProviderId = "github",
                    Title = "Review PR #482",
                    StateHash = state,
                    ViewedStateHash = viewed,
                    Status = status,
                    FetchedAt = app.Clock.UtcNow,
                    ExpiresAt = app.Clock.UtcNow.AddMinutes(10),
                }
            );
            return 0;
        });

    sealed class UnreachableTransport : IProviderHttpFactory
    {
        public IProviderHttp Create(
            IAppProvider provider,
            string? instanceUrl,
            AuthMethod method,
            ICredentialSource credentials,
            string? apiBaseUrl = null
        ) => throw new NotSupportedException("these tests never reach a provider");
    }

    sealed class UnreachableGateway : IOAuthGateway
    {
        public Task<IReadOnlyList<GatewayProvider>> ListAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<GatewayStart> StartAsync(
            string providerId,
            string codeChallenge,
            string returnTo,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Credential> RedeemAsync(
            string providerId,
            string oneTimeCode,
            string codeVerifier,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Credential> RefreshAsync(
            string providerId,
            string refreshToken,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    sealed class UnreachableDelay : IDelay
    {
        public Task DelayAsync(TimeSpan time, CancellationToken ct) =>
            throw new NotSupportedException("the rate gate should never engage with no provider");
    }
}
