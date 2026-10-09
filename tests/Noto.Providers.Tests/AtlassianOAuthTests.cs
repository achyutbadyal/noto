using Microsoft.Extensions.Logging.Abstractions;
using Noto.Core.Links;
using Noto.Providers;
using Noto.Providers.Auth;
using Noto.Providers.Providers;
using Noto.Providers.Transport;

namespace Noto.Providers.Tests;

// Atlassian sign-in on the desktop. The Noto server runs the grant under one "atlassian" app for Jira and
// Confluence. The app then picks the site from the account's accessible resources and routes requests through
// api.atlassian.com. The fake handler answers those calls, so the test can check which host each request used.
public sealed class AtlassianOAuthTests : IDisposable
{
    static readonly string Resources = """
        [
          { "id": "cloud-acme", "url": "https://acme.atlassian.net", "name": "Acme" },
          { "id": "cloud-other", "url": "https://other.atlassian.net", "name": "Other" }
        ]
        """;

    readonly Harness _h;
    readonly FakeHandler _http;
    readonly FakeOAuthGateway _gateway = new()
    {
        Providers = [new("atlassian", "Atlassian")],
        Tokens = new Credential("at-1", "rt-1", DateTimeOffset.Parse("2026-10-07T11:00:00Z")),
    };

    public AtlassianOAuthTests()
    {
        _h = new Harness([new JiraProvider(), new ConfluenceProvider()], gateway: _gateway);
        _h.Factory.ByProvider.Clear();
        _http = new FakeHandler(
            (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/oauth/token/accessible-resources")
                    return FakeHandler.Json(Resources);
                return FakeHandler.Json("""{ "displayName": "Dana" }""");
            }
        );
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
            _gateway
        );
    }

    [Fact]
    public async Task Jira_signs_in_on_the_site_the_user_names()
    {
        var connection = await Service()
            .ConnectOAuthAsync(
                "jira",
                FakeBrowser.Approve(_gateway),
                "https://other.atlassian.net"
            );

        connection.InstanceUrl.ShouldBe("https://other.atlassian.net");
        connection.ApiBaseUrl.ShouldBe("https://api.atlassian.com/ex/jira/cloud-other/");
        connection.DisplayLabel.ShouldBe("Dana");
        // Every API call after sign-in goes through the cloud gateway, not the site.
        _http
            .Calls.Select(c => c.Request)
            .Single(r => r.RequestUri!.AbsolutePath.EndsWith("/rest/api/3/myself"))
            .RequestUri!.ToString()
            .ShouldBe("https://api.atlassian.com/ex/jira/cloud-other/rest/api/3/myself");
    }

    [Fact]
    public async Task Jira_asks_the_server_for_the_shared_atlassian_app()
    {
        await Service().ConnectOAuthAsync("jira", FakeBrowser.Approve(_gateway), null);

        _gateway.Starts.Single().Provider.ShouldBe("atlassian");
        _gateway.Redeems.Single().Provider.ShouldBe("atlassian");
    }

    [Fact]
    public async Task Site_lookup_uses_the_token_the_server_handed_back()
    {
        await Service().ConnectOAuthAsync("jira", FakeBrowser.Approve(_gateway), null);

        _http
            .Calls.Select(c => c.Request)
            .Single(r => r.RequestUri!.AbsolutePath == "/oauth/token/accessible-resources")
            .Headers.Authorization!.ToString()
            .ShouldBe("Bearer at-1");
    }

    [Fact]
    public async Task A_blank_site_picks_the_first_site_on_the_account()
    {
        var connection = await Service()
            .ConnectOAuthAsync("jira", FakeBrowser.Approve(_gateway), null);

        connection.InstanceUrl.ShouldBe("https://acme.atlassian.net");
        connection.ApiBaseUrl.ShouldBe("https://api.atlassian.com/ex/jira/cloud-acme/");
    }

    [Fact]
    public async Task A_site_written_without_scheme_still_matches()
    {
        var connection = await Service()
            .ConnectOAuthAsync("jira", FakeBrowser.Approve(_gateway), "Other.Atlassian.net/");

        connection.InstanceUrl.ShouldBe("https://other.atlassian.net");
    }

    [Fact]
    public async Task A_site_the_account_cannot_reach_is_refused_with_its_name()
    {
        var e = await Should.ThrowAsync<AuthRequiredException>(() =>
            Service()
                .ConnectOAuthAsync("jira", FakeBrowser.Approve(_gateway), "missing.atlassian.net")
        );

        e.Message.ShouldContain("missing.atlassian.net");
    }

    [Fact]
    public async Task Confluence_uses_its_own_gateway_product()
    {
        var connection = await Service()
            .ConnectOAuthAsync(
                "confluence",
                FakeBrowser.Approve(_gateway),
                "https://acme.atlassian.net"
            );

        connection.ApiBaseUrl.ShouldBe("https://api.atlassian.com/ex/confluence/cloud-acme/");
    }

    [Fact]
    public async Task The_API_base_survives_a_reload_from_the_database()
    {
        var connection = await Service()
            .ConnectOAuthAsync("jira", FakeBrowser.Approve(_gateway), "other.atlassian.net");

        var stored = await _h.Uow.RunAsync(s => s.Connections.GetAsync(connection.Id));
        stored!.InstanceUrl.ShouldBe("https://other.atlassian.net");
        stored.ApiBaseUrl.ShouldBe("https://api.atlassian.com/ex/jira/cloud-other/");
    }
}
