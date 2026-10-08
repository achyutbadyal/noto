using Noto.App.Services;
using Noto.Platform.Abstractions;
using Noto.Sync;

namespace Noto.App.Tests;

// Account sign-in logic against a fake server: the session survives a restart through the keyring,
// refresh tokens rotate, and a revoked session is dropped.
public sealed class AccountServiceTests
{
    static readonly Uri Server = new("https://noto.test/");
    static readonly ServerDevice Device = new(Guid.NewGuid(), "test", "macos");

    sealed class FakeAuth : IServerAuth
    {
        public int Refreshes;
        public bool RevokeRefresh;
        public List<string> SignedOut = [];

        public Task<ServerSession> SignUpAsync(
            Uri s,
            string e,
            string p,
            string? i,
            ServerDevice d,
            CancellationToken ct
        ) => Task.FromResult(Next("signup"));

        public Task<ServerSession> SignInAsync(
            Uri s,
            string e,
            string p,
            ServerDevice d,
            CancellationToken ct
        )
        {
            if (p != "right-password")
                throw new ServerAuthException("INVALID_CREDENTIALS", "Invalid email or password");
            return Task.FromResult(Next("login"));
        }

        public Task<ServerSession> RefreshAsync(Uri s, string refreshToken, CancellationToken ct)
        {
            if (RevokeRefresh)
                throw new ServerAuthException("INVALID_REFRESH_TOKEN", "Invalid refresh token");
            Refreshes++;
            return Task.FromResult(Next($"refreshed-{Refreshes}"));
        }

        public Task SignOutAsync(Uri s, string accessToken, CancellationToken ct)
        {
            SignedOut.Add(accessToken);
            return Task.CompletedTask;
        }

        int _n;

        ServerSession Next(string tag) =>
            new(
                $"access-{tag}-{++_n}",
                $"refresh-{tag}-{_n}",
                DateTimeOffset.UtcNow.AddMinutes(15)
            );
    }

    static (
        AccountService Account,
        FakeAuth Auth,
        InMemoryKeyring Keyring,
        InMemoryUiState Ui
    ) Build(InMemoryKeyring? keyring = null, InMemoryUiState? ui = null, FakeAuth? auth = null)
    {
        keyring ??= new InMemoryKeyring();
        ui ??= new InMemoryUiState();
        auth ??= new FakeAuth();
        ui.Set("server.url", Server.ToString());
        // Connection and transaction services are not used by these tests.
        var account = new AccountService(
            auth,
            keyring,
            ui,
            Device,
            null!,
            null!,
            [],
            TimeProvider.System
        );
        return (account, auth, keyring, ui);
    }

    [Fact]
    public async Task Sign_in_stores_the_refresh_token_and_restores_after_restart()
    {
        var (account, auth, keyring, ui) = Build();
        await account.SignInAsync("me@example.com", "right-password", CancellationToken.None);
        account.Account!.Email.ShouldBe("me@example.com");
        (await keyring.GetAsync(AccountService.KeyringService, "session"))!.ShouldContain(
            "refresh-login-"
        );

        // Simulate an app restart: a new service over the same keyring and settings.
        var (restarted, _, _, _) = Build(keyring, ui, auth);
        await restarted.RestoreAsync(CancellationToken.None);
        restarted.Account!.Email.ShouldBe("me@example.com");
        auth.Refreshes.ShouldBe(1);
        // The server rotates refresh tokens, so the keychain copy is replaced.
        (await keyring.GetAsync(AccountService.KeyringService, "session"))!.ShouldContain(
            "refresh-refreshed-1"
        );
    }

    [Fact]
    public async Task A_revoked_session_is_dropped_on_restore()
    {
        var (account, auth, keyring, ui) = Build();
        await account.SignInAsync("me@example.com", "right-password", CancellationToken.None);

        auth.RevokeRefresh = true;
        var (restarted, _, _, _) = Build(keyring, ui, auth);
        await restarted.RestoreAsync(CancellationToken.None);

        restarted.Account.ShouldBeNull();
        (await keyring.GetAsync(AccountService.KeyringService, "session")).ShouldBeNull();
    }

    [Fact]
    public async Task Sign_out_revokes_on_the_server_and_forgets_locally()
    {
        var (account, auth, keyring, _) = Build();
        await account.SignInAsync("me@example.com", "right-password", CancellationToken.None);

        await account.SignOutAsync(CancellationToken.None);

        account.Account.ShouldBeNull();
        auth.SignedOut.Count.ShouldBe(1);
        (await keyring.GetAsync(AccountService.KeyringService, "session")).ShouldBeNull();
    }

    [Fact]
    public async Task Wrong_password_leaves_no_session_behind()
    {
        var (account, _, keyring, _) = Build();

        await Should.ThrowAsync<ServerAuthException>(() =>
            account.SignInAsync("me@example.com", "wrong", CancellationToken.None)
        );

        account.Account.ShouldBeNull();
        (await keyring.GetAsync(AccountService.KeyringService, "session")).ShouldBeNull();
    }

    [Fact]
    public void Server_address_must_be_http_or_https()
    {
        var (account, _, _, ui) = Build();

        Should.Throw<ArgumentException>(() => account.SetServer("ftp://noto.example.com"));
        Should.Throw<ArgumentException>(() => account.SetServer("noto.example.com"));

        account.SetServer("  https://noto.example.com/some/path  ");
        ui.Get("server.url").ShouldBe("https://noto.example.com/");
    }
}
