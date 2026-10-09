using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Noto.Core.Links;
using Noto.Providers;
using Noto.Providers.Auth;
using Noto.Providers.Providers;
using Noto.Providers.Transport;

namespace Noto.Providers.Tests;

// Browser sign-in on the desktop. The Noto server does the provider exchange and is tested on its own side. Here
// the fake server records what the app sends and returns the one-time code the browser carries back.
public sealed class OAuthSignInTests : IDisposable
{
    readonly Harness _h;
    readonly FakeHandler _http;

    public OAuthSignInTests()
    {
        _h = new Harness([new GitHubProvider()]);
        _h.Factory.ByProvider.Clear();
        // The provider's identity call, made with the tokens the server handed back.
        _http = new FakeHandler((_, _) => FakeHandler.Json("""{ "login": "dana" }"""));
    }

    public void Dispose() => _h.Dispose();

    ConnectionService Service(FakeOAuthGateway gateway)
    {
        var client = new HttpClient(_http);
        return new ConnectionService(
            _h.Uow,
            _h.Credentials,
            _h.Registry,
            new DirectTransportFactory(client, NullLogger<DirectTransport>.Instance),
            _h.Clock,
            gateway
        );
    }

    [Fact]
    public async Task Browser_sign_in_stores_the_tokens_the_server_returns()
    {
        var gateway = new FakeOAuthGateway();

        var connection = await Service(gateway)
            .ConnectOAuthAsync("github", FakeBrowser.Approve(gateway));

        connection.AuthMethod.ShouldBe(AuthMethod.OAuth2);
        connection.DisplayLabel.ShouldBe("dana");
        (await _h.Credentials.LoadAsync(connection.Id))!.AccessToken.ShouldBe("gho_abc");
    }

    [Fact]
    public async Task The_app_redeems_the_code_with_the_verifier_behind_the_challenge_it_sent()
    {
        var gateway = new FakeOAuthGateway();

        await Service(gateway).ConnectOAuthAsync("github", FakeBrowser.Approve(gateway));

        var start = gateway.Starts.Single();
        var redeem = gateway.Redeems.Single();
        redeem.Provider.ShouldBe("github");
        redeem.OneTimeCode.ShouldBe("one-time");
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(redeem.CodeVerifier)))
            .ShouldBe(start.CodeChallenge);
    }

    [Fact]
    public async Task The_return_address_is_a_loopback_callback_on_this_machine()
    {
        var gateway = new FakeOAuthGateway();

        await Service(gateway).ConnectOAuthAsync("github", FakeBrowser.Approve(gateway));

        var returnTo = new Uri(gateway.Starts.Single().ReturnTo);
        returnTo.Host.ShouldBe("127.0.0.1");
        returnTo.AbsolutePath.ShouldBe("/callback");
    }

    [Fact]
    public async Task Denied_sign_in_reports_the_reason_and_redeems_nothing()
    {
        var gateway = new FakeOAuthGateway();

        var e = await Should.ThrowAsync<AuthRequiredException>(() =>
            Service(gateway).ConnectOAuthAsync("github", FakeBrowser.Deny(gateway))
        );

        e.Message.ShouldContain("AUTHORIZATION_DENIED");
        gateway.Redeems.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_server_without_a_session_stops_the_flow_before_the_browser_opens()
    {
        var gateway = new FakeOAuthGateway
        {
            StartError = new AuthRequiredException(
                "Sign in to your Noto server first (Settings, Account and sync)."
            ),
        };
        var opened = false;

        var e = await Should.ThrowAsync<AuthRequiredException>(() =>
            Service(gateway)
                .ConnectOAuthAsync(
                    "github",
                    _ =>
                    {
                        opened = true;
                        return Task.CompletedTask;
                    }
                )
        );

        e.Message.ShouldContain("Sign in to your Noto server");
        opened.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelling_while_waiting_for_the_browser_stops_the_wait()
    {
        var gateway = new FakeOAuthGateway();
        using var cancel = new CancellationTokenSource();
        Func<Uri, Task> cancelInBrowser = _ =>
        {
            cancel.Cancel();
            return Task.CompletedTask;
        };

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Service(gateway).ConnectOAuthAsync("github", cancelInBrowser, ct: cancel.Token)
        );
    }

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
