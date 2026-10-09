using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Noto.Core.Links;
using Noto.Providers;
using Noto.Providers.Auth;
using Noto.Providers.Providers;
using Noto.Providers.Transport;

namespace Noto.Providers.Tests;

// Connecting with a pasted token goes through the real direct transport, so these exercise the same
// URL building and auth headers the app uses. Responses come from a fake HTTP handler.
public sealed class ConnectTokenTests : IDisposable
{
    readonly Harness _h;
    readonly Recorder _http = new();

    public ConnectTokenTests()
    {
        _h = new Harness([
            new JiraProvider(),
            new ConfluenceProvider(),
            new LinearProvider(),
            new GitHubProvider(),
        ]);
        _h.Factory.ByProvider.Clear();
    }

    public void Dispose() => _h.Dispose();

    ConnectionService Service()
    {
        var client = new HttpClient(_http);
        return new ConnectionService(
            _h.Uow,
            _h.Credentials,
            _h.Registry,
            new DirectTransportFactory(client, NullLogger<DirectTransport>.Instance),
            _h.Clock,
            _h.Gateway
        );
    }

    [Fact]
    public async Task Jira_token_without_a_site_address_is_a_clear_error_not_a_crash()
    {
        var e = await Should.ThrowAsync<ArgumentException>(() =>
            Service().ConnectWithTokenAsync("jira", "token", AuthMethod.PersonalToken)
        );
        e.Message.ShouldContain("site address");
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Jira_api_key_connects_with_site_and_email()
    {
        _http.Reply("/rest/api/3/myself", """{ "displayName": "Dana" }""");

        var c = await Service()
            .ConnectWithTokenAsync(
                "jira",
                "api-token",
                AuthMethod.ApiKey,
                instanceUrl: "https://acme.atlassian.net",
                username: "dana@acme.com"
            );

        c.DisplayLabel.ShouldBe("Dana");
        c.InstanceUrl.ShouldBe("https://acme.atlassian.net");
        _http
            .Requests.Single()
            .RequestUri!.ToString()
            .ShouldBe("https://acme.atlassian.net/rest/api/3/myself");
        _http.Requests.Single().Headers.Authorization!.Scheme.ShouldBe("Basic");
    }

    [Fact]
    public async Task Jira_api_key_without_email_is_a_clear_error()
    {
        var e = await Should.ThrowAsync<ArgumentException>(() =>
            Service()
                .ConnectWithTokenAsync(
                    "jira",
                    "api-token",
                    AuthMethod.ApiKey,
                    instanceUrl: "https://acme.atlassian.net"
                )
        );
        e.Message.ShouldContain("email");
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Instance_address_must_be_an_absolute_http_url()
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            Service()
                .ConnectWithTokenAsync(
                    "jira",
                    "api-token",
                    AuthMethod.ApiKey,
                    instanceUrl: "acme.atlassian.net",
                    username: "dana@acme.com"
                )
        );
    }

    [Fact]
    public async Task Linear_api_key_connects_without_an_instance()
    {
        _http.Reply(
            "graphql",
            """{ "data": { "viewer": { "name": "Dana", "email": "dana@acme.com" } } }"""
        );

        var c = await Service().ConnectWithTokenAsync("linear", "lin_api_xyz", AuthMethod.ApiKey);

        c.DisplayLabel.ShouldBe("Dana");
        _http.Requests.Single().RequestUri!.ToString().ShouldBe("https://api.linear.app/graphql");
    }

    // A minimal HttpMessageHandler: answers by path suffix, records every request.
    sealed class Recorder : HttpMessageHandler
    {
        readonly Dictionary<string, string> _replies = [];
        public List<HttpRequestMessage> Requests { get; } = [];

        public void Reply(string pathSuffix, string json) => _replies[pathSuffix] = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            var body = _replies.FirstOrDefault(r => path.EndsWith(r.Key)).Value ?? "{}";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        body,
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
