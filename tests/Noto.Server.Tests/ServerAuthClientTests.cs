using Noto.Sync;

namespace Noto.Server.Tests;

// The desktop's auth client, run against the real server pipeline in-process.
public sealed class ServerAuthClientTests : IDisposable
{
    readonly ServerFactory _f = new(
        new() { ["REGISTRATION"] = "invite", ["INVITE_CODES"] = "invite-code-0123456789" }
    );
    readonly ServerAuthClient _client;
    readonly Uri _server = new("https://noto.test/");

    public ServerAuthClientTests() => _client = new ServerAuthClient(_f.CreateClient());

    public void Dispose() => _f.Dispose();

    static ServerDevice Device() => new(Guid.NewGuid(), "Test laptop", "macos");

    [Fact]
    public async Task Sign_up_sign_in_refresh_and_sign_out_round_trip()
    {
        var signedUp = await _client.SignUpAsync(
            _server,
            "me@example.com",
            "correct horse battery",
            "invite-code-0123456789",
            Device(),
            default
        );
        signedUp.AccessToken.ShouldNotBeNullOrEmpty();

        var signedIn = await _client.SignInAsync(
            _server,
            "me@example.com",
            "correct horse battery",
            Device(),
            default
        );
        var refreshed = await _client.RefreshAsync(_server, signedIn.RefreshToken, default);
        refreshed.RefreshToken.ShouldNotBe(signedIn.RefreshToken);

        await _client.SignOutAsync(_server, refreshed.AccessToken, default);
        // Refresh tokens rotate, and reusing a rotated one is treated as theft (reuse detection).
        var reuse = await Should.ThrowAsync<ServerAuthException>(() =>
            _client.RefreshAsync(_server, signedIn.RefreshToken, default)
        );
        reuse.Code.ShouldBe("REFRESH_TOKEN_REUSED");
    }

    [Fact]
    public async Task Server_error_codes_reach_the_caller()
    {
        var noCode = await Should.ThrowAsync<ServerAuthException>(() =>
            _client.SignUpAsync(
                _server,
                "me@example.com",
                "correct horse battery",
                null,
                Device(),
                default
            )
        );
        noCode.Code.ShouldBe("INVALID_INVITE");

        await _client.SignUpAsync(
            _server,
            "taken@example.com",
            "correct horse battery",
            "invite-code-0123456789",
            Device(),
            default
        );
        var taken = await Should.ThrowAsync<ServerAuthException>(() =>
            _client.SignUpAsync(
                _server,
                "taken@example.com",
                "correct horse battery",
                "invite-code-0123456789",
                Device(),
                default
            )
        );
        taken.Code.ShouldBe("EMAIL_TAKEN");

        var wrong = await Should.ThrowAsync<ServerAuthException>(() =>
            _client.SignInAsync(_server, "taken@example.com", "not the password", Device(), default)
        );
        wrong.Code.ShouldBe("INVALID_CREDENTIALS");
    }
}
