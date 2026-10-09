using Noto.App.Services;
using Noto.Platform.Abstractions;
using Noto.Providers.Auth;
using Noto.Sync;

namespace Noto.App.Tests;

// An account service with a live session on a fake server, and the providers that server is set up for. Browser
// sign-in offers only providers the signed-in server can run, so tests of that list need a session.
public static class SignedInAccount
{
    public static async Task<AccountService> CreateAsync(
        IUiState ui,
        IReadOnlyList<OAuthOption> candidates,
        IReadOnlyList<string> serverProviders,
        bool signedIn = true,
        IReadOnlyList<TokenOption>? tokens = null
    )
    {
        ui.Set("server.url", "https://noto.test/");
        var account = new AccountService(
            new FakeServer(),
            new Noto.Platform.Abstractions.InMemoryKeyring(),
            ui,
            new ServerDevice(Guid.NewGuid(), "test", "macos"),
            null!,
            null!,
            tokens ?? [],
            TimeProvider.System,
            candidates,
            _ => Task.CompletedTask,
            _ =>
                Task.FromResult<IReadOnlyList<GatewayProvider>>(
                    serverProviders.Select(id => new GatewayProvider(id, id)).ToList()
                )
        );
        if (signedIn)
            await account.SignInAsync("dana@acme.test", "password", default);
        return account;
    }

    sealed class FakeServer : IServerAuth
    {
        public Task<ServerSession> SignUpAsync(
            Uri s,
            string e,
            string p,
            string? i,
            ServerDevice d,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ServerSession> SignInAsync(
            Uri s,
            string e,
            string p,
            ServerDevice d,
            CancellationToken ct
        ) =>
            Task.FromResult(
                new ServerSession("access-1", "refresh-1", DateTimeOffset.UtcNow.AddMinutes(15))
            );

        public Task<ServerSession> RefreshAsync(Uri s, string r, CancellationToken ct) =>
            Task.FromResult(
                new ServerSession("access-2", "refresh-2", DateTimeOffset.UtcNow.AddMinutes(15))
            );

        public Task SignOutAsync(Uri s, string a, CancellationToken ct) => Task.CompletedTask;
    }
}
