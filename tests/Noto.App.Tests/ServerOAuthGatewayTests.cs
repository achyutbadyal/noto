using System.Net;
using System.Text;
using Noto.App.Services;
using Noto.Providers.Auth;

namespace Noto.App.Tests;

// The desktop's view of the server's OAuth broker: the paths, the session it sends, and how errors read.
public sealed class ServerOAuthGatewayTests
{
    static readonly Uri Server = new("https://noto.test/");

    static (ServerOAuthGateway Gateway, Recorder Http) Build(HttpStatusCode status, string body)
    {
        var http = new Recorder(status, body);
        return (
            new ServerOAuthGateway(
                new HttpClient(http),
                _ => Task.FromResult((Server, "session-token"))
            ),
            http
        );
    }

    [Fact]
    public async Task Start_sends_the_session_the_challenge_and_the_loopback_return_address()
    {
        var (gateway, http) = Build(
            HttpStatusCode.OK,
            """{ "authorize_url": "https://auth.provider.test/authorize", "state": "s-1" }"""
        );

        var start = await gateway.StartAsync(
            "atlassian",
            "challenge-1",
            "http://127.0.0.1:53124/callback",
            default
        );

        start.State.ShouldBe("s-1");
        start.AuthorizeUrl.ShouldBe("https://auth.provider.test/authorize");
        http.Last.RequestUri!.ToString()
            .ShouldBe("https://noto.test/v1/gateway/oauth/atlassian/start");
        http.Last.Headers.Authorization!.ToString().ShouldBe("Bearer session-token");
        http.LastBody.ShouldContain("\"code_challenge\":\"challenge-1\"");
        http.LastBody.ShouldContain("\"return_to\":\"http://127.0.0.1:53124/callback\"");
    }

    [Fact]
    public async Task Redeem_turns_the_server_tokens_into_a_credential()
    {
        var (gateway, _) = Build(
            HttpStatusCode.OK,
            """
            { "access_token": "at", "refresh_token": "rt", "expires_at": "2026-10-07T11:00:00Z",
              "scopes": "read", "display_label": "Atlassian" }
            """
        );

        var credential = await gateway.RedeemAsync("atlassian", "one-time", "verifier", default);

        credential.AccessToken.ShouldBe("at");
        credential.RefreshToken.ShouldBe("rt");
        credential.ExpiresAt.ShouldBe(DateTimeOffset.Parse("2026-10-07T11:00:00Z"));
    }

    [Fact]
    public async Task A_refused_request_shows_the_servers_title()
    {
        var (gateway, _) = Build(
            HttpStatusCode.BadRequest,
            """{ "title": "return_to must be a http://127.0.0.1:<port>/callback address", "code": "INVALID_RETURN_TO" }"""
        );

        var e = await Should.ThrowAsync<AuthRequiredException>(() =>
            gateway.StartAsync("github", "c", "http://example.com/callback", default)
        );

        e.Message.ShouldBe("return_to must be a http://127.0.0.1:<port>/callback address");
    }

    [Fact]
    public async Task An_expired_session_asks_the_user_to_sign_in_again()
    {
        var (gateway, _) = Build(HttpStatusCode.Unauthorized, "");

        var e = await Should.ThrowAsync<AuthRequiredException>(() => gateway.ListAsync(default));

        e.Message.ShouldContain("sign in to the server again");
    }

    sealed class Recorder(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage Last { get; private set; } = null!;
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Last = request;
            LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
