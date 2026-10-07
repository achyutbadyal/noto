using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Noto.Core.Links;
using Noto.Providers.Auth;
using Noto.Providers.Providers;
using Noto.Providers.Transport;

namespace Noto.Providers.Tests;

public class OAuthTests
{
    static readonly AuthConfig Config = new(
        "https://auth.test/authorize",
        "https://auth.test/token",
        ["read", "write"],
        ""
    );
    static readonly OAuthClient Client = new("cid", "csecret");
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-07T10:00:00Z");

    [Fact]
    public void Pkce_challenge_matches_the_rfc7636_example()
    {
        // RFC 7636 appendix B
        var challenge = Convert
            .ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"u8.ToArray()
                )
            )
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        challenge.ShouldBe("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }

    [Fact]
    public void Generated_pkce_pair_is_consistent_and_url_safe()
    {
        var (verifier, challenge) = Pkce.Create();
        verifier.Length.ShouldBeInRange(43, 128);
        (verifier + challenge).IndexOfAny(new[] { '+', '/', '=' }).ShouldBe(-1);
        var expected = Convert
            .ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.ASCII.GetBytes(verifier)
                )
            )
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        challenge.ShouldBe(expected);
        Pkce.Create().Verifier.ShouldNotBe(verifier);
    }

    [Fact]
    public void Authorize_url_carries_pkce_state_and_loopback_redirect()
    {
        var url = OAuthFlow.BuildAuthorizeUrl(
            Config,
            Client,
            "http://127.0.0.1:5000/callback",
            "st4te",
            "chal"
        );
        var q = System.Web.HttpUtility.ParseQueryString(url.Query);

        (q["client_id"], q["state"], q["code_challenge"], q["code_challenge_method"]).ShouldBe(
            ("cid", "st4te", "chal", "S256")
        );
        q["redirect_uri"].ShouldBe("http://127.0.0.1:5000/callback");
        q["scope"].ShouldBe("read write");
    }

    [Fact]
    public async Task Code_exchange_sends_verifier_and_parses_tokens()
    {
        var handler = new FakeHandler(
            (_, _) =>
                FakeHandler.Json(
                    """{ "access_token": "at", "refresh_token": "rt", "expires_in": 3600 }"""
                )
        );
        var cred = await OAuthFlow.ExchangeCodeAsync(
            new HttpClient(handler),
            Config,
            Client,
            "the-code",
            "http://127.0.0.1:1/callback",
            "ver",
            Now,
            default
        );

        cred.ShouldBe(new Credential("at", "rt", Now.AddHours(1)));
        var body = handler.Calls.Single().Body;
        body.ShouldContain("grant_type=authorization_code");
        body.ShouldContain("code_verifier=ver");
        body.ShouldContain("client_secret=csecret");
    }

    [Fact]
    public async Task Refresh_keeps_the_old_refresh_token_when_none_is_returned()
    {
        var handler = new FakeHandler(
            (_, _) => FakeHandler.Json("""{ "access_token": "at2", "expires_in": 60 }""")
        );
        var cred = await OAuthFlow.RefreshAsync(
            new HttpClient(handler),
            Config,
            Client,
            "old-rt",
            Now,
            default
        );
        (cred.AccessToken, cred.RefreshToken).ShouldBe(("at2", "old-rt"));
    }

    [Fact]
    public async Task Token_endpoint_failure_requires_reauth()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("{}", 400));
        await Should.ThrowAsync<AuthRequiredException>(() =>
            OAuthFlow.RefreshAsync(new HttpClient(handler), Config, Client, "rt", Now, default)
        );
    }

    [Fact]
    public async Task Slack_style_nested_user_token_is_read()
    {
        var handler = new FakeHandler(
            (_, _) =>
                FakeHandler.Json("""{ "ok": true, "authed_user": { "access_token": "xoxp-1" } }""")
        );
        var cred = await OAuthFlow.ExchangeCodeAsync(
            new HttpClient(handler),
            Config with
            {
                UsesPkce = false,
            },
            Client,
            "c",
            "http://127.0.0.1:1/callback",
            "v",
            Now,
            default
        );
        cred.AccessToken.ShouldBe("xoxp-1");
    }

    [Fact]
    public async Task Loopback_receiver_returns_the_code_for_a_matching_state()
    {
        using var receiver = new LoopbackReceiver();
        receiver.RedirectUri.ShouldStartWith("http://127.0.0.1:");
        var wait = receiver.WaitForCodeAsync("abc", default);

        using var http = new HttpClient();
        (
            await http.GetAsync($"{receiver.RedirectUri}?code=the-code&state=abc")
        ).EnsureSuccessStatusCode();

        (await wait).ShouldBe("the-code");
    }

    [Fact]
    public async Task Loopback_receiver_rejects_a_state_mismatch()
    {
        using var receiver = new LoopbackReceiver();
        var wait = receiver.WaitForCodeAsync("expected", default);

        using var http = new HttpClient();
        await http.GetAsync($"{receiver.RedirectUri}?code=x&state=forged");

        await Should.ThrowAsync<AuthRequiredException>(() => wait);
    }

    [Fact]
    public async Task Loopback_receiver_reports_user_denial_and_ignores_stray_requests()
    {
        using var receiver = new LoopbackReceiver();
        var wait = receiver.WaitForCodeAsync("s", default);

        using var http = new HttpClient();
        await http.GetAsync(receiver.RedirectUri.Replace("/callback", "/favicon.ico"));
        wait.IsCompleted.ShouldBeFalse();
        await http.GetAsync($"{receiver.RedirectUri}?error=access_denied&state=s");

        (await Should.ThrowAsync<AuthRequiredException>(() => wait)).Message.ShouldContain(
            "access_denied"
        );
    }
}

public class TokenManagerTests
{
    static (Harness H, ScriptedProvider P, FakeHandler Oauth) Build(
        Func<HttpRequestMessage, string, HttpResponseMessage>? reply = null
    )
    {
        var oauth = new FakeHandler(
            reply
                ?? (
                    (_, _) =>
                        FakeHandler.Json(
                            """{ "access_token": "fresh", "refresh_token": "rt2", "expires_in": 3600 }"""
                        )
                )
        );
        var provider = new ScriptedProvider
        {
            Config = new("https://auth.test/authorize", "https://auth.test/token", [], ""),
        };
        var harness = new Harness(
            [provider],
            oauthHandler: oauth,
            oauthClients: new Dictionary<string, OAuthClient> { ["fake"] = new("cid") }
        );
        return (harness, provider, oauth);
    }

    [Fact]
    public async Task Valid_token_is_returned_without_refreshing()
    {
        var (h, _, oauth) = Build();
        var c = await h.ConnectAsync("fake");
        await h.Credentials.SaveAsync(
            c.Id,
            new Credential("live", "rt", h.Clock.UtcNow.AddHours(1))
        );

        (await h.Tokens.GetValidAsync(c.Id, default)).AccessToken.ShouldBe("live");
        oauth.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_callers_trigger_exactly_one_refresh()
    {
        var (h, _, oauth) = Build();
        var c = await h.ConnectAsync("fake");
        await h.Credentials.SaveAsync(
            c.Id,
            new Credential("old", "rt", h.Clock.UtcNow.AddSeconds(30))
        );

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => h.Tokens.GetValidAsync(c.Id, default))
        );

        oauth.Calls.Count.ShouldBe(1);
        results.ShouldAllBe(r => r.AccessToken == "fresh");
        (await h.Credentials.LoadAsync(c.Id))!.RefreshToken.ShouldBe("rt2");
    }

    [Fact]
    public async Task Failed_refresh_marks_the_connection_refresh_failed()
    {
        var (h, _, _) = Build((_, _) => FakeHandler.Json("{}", 400));
        var c = await h.ConnectAsync("fake");
        await h.Credentials.SaveAsync(
            c.Id,
            new Credential("old", "rt", h.Clock.UtcNow.AddSeconds(-5))
        );

        await Should.ThrowAsync<AuthRequiredException>(() => h.Tokens.GetValidAsync(c.Id, default));

        (await h.Uow.RunAsync(s => s.Connections.GetAsync(c.Id)))!.Status.ShouldBe(
            ConnectionStatus.RefreshFailed
        );
    }

    [Fact]
    public async Task Expired_pat_without_refresh_token_marks_the_connection_expired()
    {
        var (h, _, _) = Build();
        var c = await h.ConnectAsync("fake");
        await h.Credentials.SaveAsync(
            c.Id,
            new Credential("pat", ExpiresAt: h.Clock.UtcNow.AddDays(-1))
        );

        await Should.ThrowAsync<AuthRequiredException>(() => h.Tokens.GetValidAsync(c.Id, default));

        (await h.Uow.RunAsync(s => s.Connections.GetAsync(c.Id)))!.Status.ShouldBe(
            ConnectionStatus.Expired
        );
    }
}

public class DirectTransportTests
{
    static DirectTransport Make(
        FakeHandler handler,
        IAppProvider provider,
        string? instance,
        AuthMethod method,
        Credential? cred
    ) =>
        new(
            new HttpClient(handler),
            provider,
            instance,
            method,
            new StaticCredentialSource(cred),
            NullLogger.Instance
        );

    [Fact]
    public async Task Resolves_against_the_api_root_and_attaches_the_token()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("""{ "data": {} }"""));
        var t = Make(
            handler,
            new GitHubProvider(),
            null,
            AuthMethod.PersonalToken,
            new Credential("ghp_x")
        );

        await t.SendAsync(ProviderRequest.Post("graphql", "{}"), default);

        var (req, body) = handler.Calls.Single();
        req.RequestUri!.ToString().ShouldBe("https://api.github.com/graphql");
        req.Headers.Authorization!.ToString().ShouldBe("Bearer ghp_x");
        body.ShouldBe("{}");
    }

    [Fact]
    public async Task Enterprise_instance_changes_the_root_and_query_is_escaped()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("{}"));
        var t = Make(
            handler,
            new GitHubProvider(),
            "https://ghe.acme.com",
            AuthMethod.PersonalToken,
            new Credential("t")
        );

        await t.SendAsync(ProviderRequest.Get("v3/search", ("q", "a b&c")), default);

        handler
            .Calls.Single()
            .Request.RequestUri!.AbsoluteUri.ShouldBe(
                "https://ghe.acme.com/api/v3/search?q=a%20b%26c"
            );
    }

    [Fact]
    public async Task Provider_specific_auth_scheme_is_used()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("{}"));
        var t = Make(
            handler,
            new LinearProvider(),
            null,
            AuthMethod.ApiKey,
            new Credential("lin_key")
        );

        await t.SendAsync(ProviderRequest.Post("graphql", "{}"), default);

        handler
            .Calls.Single()
            .Request.Headers.GetValues("Authorization")
            .Single()
            .ShouldBe("lin_key");
    }

    [Fact]
    public async Task Failures_return_the_status_and_retry_after()
    {
        var handler = new FakeHandler(
            (_, _) =>
            {
                var r = FakeHandler.Json("{}", 429);
                r.Headers.Add("Retry-After", "17");
                return r;
            }
        );
        var t = Make(
            handler,
            new GitHubProvider(),
            null,
            AuthMethod.PersonalToken,
            new Credential("t")
        );

        var ex = await Should.ThrowAsync<ProviderHttpException>(() =>
            t.GetJsonAsync(ProviderRequest.Post("graphql", "{}"), default)
        );

        (ex.Status, ex.RetryAfter).ShouldBe((429, TimeSpan.FromSeconds(17)));
    }

    [Fact]
    public async Task Gateway_posts_the_provider_request_with_token_in_a_separate_header()
    {
        var handler = new FakeHandler(
            (_, _) =>
                FakeHandler.Json(
                    """{ "status": 200, "headers": { "X-Test": "1" }, "body": { "ok": true } }"""
                )
        );
        var provider = new GitHubProvider();
        var t = new GatewayTransport(
            new HttpClient(handler),
            new Uri("https://api.noto.test/v1/"),
            _ => Task.FromResult("noto-jwt"),
            provider,
            null,
            AuthMethod.PersonalToken,
            new StaticCredentialSource(new Credential("ghp_x"))
        );

        var response = await t.SendAsync(ProviderRequest.Get("user", ("a", "b")), default);

        var (req, body) = handler.Calls.Single();
        req.RequestUri!.ToString().ShouldBe("https://api.noto.test/v1/gateway/fetch");
        req.Headers.Authorization!.ToString().ShouldBe("Bearer noto-jwt");
        req.Headers.GetValues("X-Provider-Authorization").Single().ShouldBe("Bearer ghp_x");
        body.ShouldContain("\"provider_id\":\"github\"");
        body.ShouldContain("\"path\":\"user\"");
        response.Status.ShouldBe(200);
        response.Body.ShouldContain("\"ok\"");
        response.Headers["X-Test"].ShouldBe("1");
    }

    [Fact]
    public async Task Gateway_opengraph_endpoint()
    {
        var handler = new FakeHandler(
            (_, _) =>
                FakeHandler.Json(
                    """{ "title": "T", "description": "D", "image": null, "site_name": "S" }"""
                )
        );
        var t = new GatewayTransport(
            new HttpClient(handler),
            new Uri("https://api.noto.test/v1/"),
            _ => Task.FromResult("jwt"),
            new OpenGraphProvider(),
            null,
            AuthMethod.PersonalToken,
            new StaticCredentialSource(null)
        );

        var og = await t.OpenGraphAsync(new Uri("https://example.com"), default);

        (og!.Title, og.SiteName).ShouldBe(("T", "S"));
        handler.Calls.Single().Request.RequestUri!.AbsolutePath.ShouldBe("/v1/gateway/opengraph");
    }
}
