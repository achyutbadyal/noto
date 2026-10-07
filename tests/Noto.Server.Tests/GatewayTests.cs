using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Noto.Server.Gateway;

namespace Noto.Server.Tests;

public class GraphQlGuardTests
{
    [Theory]
    [InlineData("{ viewer { login } }")]
    [InlineData(
        "query Q($n: Int) { repository(name: \"x\") { issues(first: $n) { nodes { title } } } }"
    )]
    [InlineData("query { a }  # mutation in a comment\n")]
    [InlineData("query { search(query: \"mutation { x }\") { id } }")] // keyword only inside a string
    [InlineData("query { mutation }")] // a field *named* mutation
    [InlineData("fragment F on User { id } query { viewer { ...F } }")]
    [InlineData("query { a(arg: \"\"\"mutation\"\"\") }")]
    public void Allows_read_only_queries(string query) =>
        GraphQlGuard.IsReadOnly(query).ShouldBeTrue();

    [Theory]
    [InlineData("mutation { deleteRepo(id: 1) { ok } }")]
    [InlineData("mutation M { x }")]
    [InlineData("subscription { events }")]
    [InlineData("query { a } mutation { b }")] // mixed document
    [InlineData("query { a }\nmutation\n{ b }")]
    [InlineData("  mutation { b }  ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("query { a")] // unbalanced
    [InlineData("fragment F on User { id }")] // no operation at all
    public void Rejects_everything_else(string query) =>
        GraphQlGuard.IsReadOnly(query).ShouldBeFalse();

    [Fact]
    public void Request_bodies_must_be_a_single_json_object_with_a_query()
    {
        GraphQlGuard.IsReadOnlyRequest("""{"query":"{ viewer { id } }"}""").ShouldBeTrue();
        GraphQlGuard.IsReadOnlyRequest("""{"query":"mutation { x }"}""").ShouldBeFalse();
        GraphQlGuard.IsReadOnlyRequest("""[{"query":"{ a }"},{"query":"{ b }"}]""").ShouldBeFalse(); // batching refused
        GraphQlGuard.IsReadOnlyRequest("""{"noquery":true}""").ShouldBeFalse();
        GraphQlGuard.IsReadOnlyRequest("not json").ShouldBeFalse();
    }
}

public class HostPatternTests
{
    [Theory]
    [InlineData("api.github.com", "api.github.com", true)]
    [InlineData("api.github.com", "evil.github.com", false)]
    [InlineData("*.atlassian.net", "acme.atlassian.net", true)]
    [InlineData("*.atlassian.net", "a.b.atlassian.net", false)]
    [InlineData("*.atlassian.net", "atlassian.net", false)]
    [InlineData("*.atlassian.net", "acme.atlassian.net.evil.com", false)]
    [InlineData("*.atlassian.net", "evilatlassian.net", false)]
    public void Matches(string pattern, string host, bool expected) =>
        HostPattern.Matches(pattern, host).ShouldBe(expected);
}

public sealed class GatewayTests : IDisposable
{
    readonly ServerFactory _f = new();

    public void Dispose() => _f.Dispose();

    static StringContent Json(object o) =>
        new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    static async Task<JsonElement> Body(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    async Task<HttpResponseMessage> Fetch(
        ServerFactory.Session s,
        object request,
        string? providerToken = "Bearer provider-token"
    )
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/v1/gateway/fetch")
        {
            Content = Json(request),
        };
        if (providerToken is not null)
            message.Headers.TryAddWithoutValidation("X-Provider-Authorization", providerToken);
        return await s.Client.SendAsync(message);
    }

    static object Req(
        string provider,
        string path,
        string method = "GET",
        object? query = null,
        object? body = null,
        string? instance = null
    ) =>
        new
        {
            provider_id = provider,
            instance_url = instance,
            request = new
            {
                method,
                path,
                query,
                body,
            },
        };

    // --- fetch ---------------------------------------------------------------------------

    [Fact]
    public async Task Requires_a_signed_in_user()
    {
        var anonymous = _f.CreateClient();
        var response = await anonymous.PostAsync("/v1/gateway/fetch", Json(Req("github", "/user")));
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (
            await anonymous.PostAsync(
                "/v1/gateway/opengraph",
                Json(new { url = "https://example.com" })
            )
        ).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Forwards_a_get_with_the_provider_token_and_returns_status_headers_and_body()
    {
        var s = await _f.RegisterAsync();
        _f.Upstream.Respond = _ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"merged":true}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
            r.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "4999");
            r.Headers.TryAddWithoutValidation("Set-Cookie", "session=leak");
            return r;
        };

        var response = await Fetch(
            s,
            Req("github", "/repos/acme/app/pulls/7", query: new { state = "all" })
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await Body(response);
        json.GetProperty("status").GetInt32().ShouldBe(200);
        json.GetProperty("body").GetString().ShouldBe("""{"merged":true}""");
        json.GetProperty("headers")
            .GetProperty("x-ratelimit-remaining")
            .GetString()
            .ShouldBe("4999");
        json.GetProperty("headers").TryGetProperty("set-cookie", out _).ShouldBeFalse(); // only an allowlist of headers is exposed

        var upstream = _f.Upstream.Requests.Single();
        upstream.Method.ShouldBe(HttpMethod.Get);
        upstream
            .RequestUri!.ToString()
            .ShouldBe("https://api.github.com/repos/acme/app/pulls/7?state=all");
        upstream.Headers.GetValues("Authorization").Single().ShouldBe("Bearer provider-token");
    }

    [Fact]
    public async Task The_noto_bearer_token_is_never_sent_upstream()
    {
        var s = await _f.RegisterAsync();
        await Fetch(
            s,
            Req("linear", "/graphql", "POST", body: new { query = "{ viewer { id } }" }),
            providerToken: null
        );

        _f.Upstream.Requests.Single().Headers.Contains("Authorization").ShouldBeFalse();
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Writes_are_refused_outside_graphql(string method)
    {
        var s = await _f.RegisterAsync();

        var response = await Fetch(
            s,
            Req("github", "/repos/acme/app/issues", method, body: new { title = "x" })
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Body(response)).GetProperty("code").GetString().ShouldBe("METHOD_NOT_ALLOWED");
        _f.Upstream.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Graphql_queries_pass_but_mutations_do_not()
    {
        var s = await _f.RegisterAsync();

        var ok = await Fetch(
            s,
            Req("github", "/graphql", "POST", body: new { query = "query { viewer { login } }" })
        );
        var bad = await Fetch(
            s,
            Req(
                "github",
                "/graphql",
                "POST",
                body: new
                {
                    query = "mutation { addStar(input:{starrableId:\"x\"}) { clientMutationId } }",
                }
            )
        );

        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        bad.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Body(bad)).GetProperty("code").GetString().ShouldBe("GRAPHQL_NOT_READ_ONLY");
        _f.Upstream.Requests.Count.ShouldBe(1);
        _f.Upstream.Requests[0].Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task Post_to_a_non_graphql_path_on_a_graphql_provider_is_refused()
    {
        var s = await _f.RegisterAsync();
        var response = await Fetch(
            s,
            Req("github", "/repos/a/b/issues", "POST", body: new { query = "{ a }" })
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("//evil.com/x")]
    [InlineData("/../etc/passwd")]
    [InlineData("/repos/%2e%2e/%2e%2e/admin")]
    [InlineData("/a\\b")]
    [InlineData("relative/path")]
    [InlineData("/redirect?u=http://evil.com")] // a query string smuggled into the path
    [InlineData("/x://evil")]
    public async Task Malicious_paths_never_reach_the_upstream(string path)
    {
        var s = await _f.RegisterAsync();

        var response = await Fetch(s, Req("github", path));

        ((int)response.StatusCode).ShouldBeInRange(400, 403);
        if (path == "/redirect?u=http://evil.com")
            return; // encoded into the path, so it stays on api.github.com
        _f.Upstream.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_path_with_a_question_mark_cannot_inject_a_host_or_query()
    {
        var s = await _f.RegisterAsync();
        await Fetch(s, Req("github", "/user?x=1#frag"));

        var uri = _f.Upstream.Requests.SingleOrDefault()?.RequestUri;
        if (uri is not null)
            uri.Host.ShouldBe("api.github.com");
    }

    [Fact]
    public async Task Unknown_providers_are_refused()
    {
        var s = await _f.RegisterAsync();
        (await Fetch(s, Req("evilcorp", "/x"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("github", "https://evil.example.com")] // github has no self-hosted instances
    [InlineData("linear", "https://evil.example.com")]
    [InlineData("figma", "https://acme.atlassian.net")]
    [InlineData("github", "http://api.github.com")]
    [InlineData("github", "https://user:pw@api.github.com")]
    public async Task Instance_urls_outside_the_allowlist_are_refused(
        string provider,
        string instance
    )
    {
        var s = await _f.RegisterAsync();
        var response = await Fetch(s, Req(provider, "/x", instance: instance));
        ((int)response.StatusCode).ShouldBeInRange(400, 403);
        _f.Upstream.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Atlassian_cloud_sites_and_self_hosted_gitlab_are_reachable()
    {
        var s = await _f.RegisterAsync();

        (
            await Fetch(
                s,
                Req("atlassian", "/rest/api/3/issue/ENG-1", instance: "https://acme.atlassian.net")
            )
        ).StatusCode.ShouldBe(HttpStatusCode.OK);
        (
            await Fetch(
                s,
                Req(
                    "gitlab",
                    "/api/v4/projects/1/merge_requests/2",
                    instance: "https://gitlab.acme.dev"
                )
            )
        ).StatusCode.ShouldBe(HttpStatusCode.OK);

        _f.Upstream.Requests.Select(r => r.RequestUri!.Host)
            .ShouldBe(["acme.atlassian.net", "gitlab.acme.dev"]);
    }

    [Fact]
    public async Task Self_hosted_instances_on_private_addresses_are_blocked_unless_the_operator_allows_them()
    {
        _f.Dns.Records["gitlab.acme.internal"] = [IPAddress.Parse("10.0.0.7")];
        var s = await _f.RegisterAsync();
        var blocked = await Fetch(
            s,
            Req("gitlab", "/api/v4/user", instance: "https://gitlab.acme.internal")
        );
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var open = new ServerFactory(
            new() { ["GATEWAY_ALLOW_PRIVATE_HOSTS"] = "gitlab.acme.internal" }
        );
        open.Dns.Records["gitlab.acme.internal"] = [IPAddress.Parse("10.0.0.7")];
        var s2 = await open.RegisterAsync();
        var message = new HttpRequestMessage(HttpMethod.Post, "/v1/gateway/fetch")
        {
            Content = Json(Req("gitlab", "/api/v4/user", instance: "https://gitlab.acme.internal")),
        };
        (await s2.Client.SendAsync(message)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Slack_is_limited_to_its_api_path()
    {
        var s = await _f.RegisterAsync();
        (
            await Fetch(
                s,
                Req("slack", "/api/conversations.history", query: new { channel = "C1" })
            )
        ).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Fetch(s, Req("slack", "/files/secret"))).StatusCode.ShouldBe(
            HttpStatusCode.Forbidden
        );
    }

    [Fact]
    public async Task Upstream_redirects_to_other_hosts_are_not_followed()
    {
        var s = await _f.RegisterAsync();
        _f.Upstream.Respond = _ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri("https://evil.example.com/steal");
            return r;
        };

        var response = await Fetch(s, Req("github", "/user"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Body(response)).GetProperty("code").GetString().ShouldBe("REDIRECT_NOT_ALLOWED");
        _f.Upstream.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Upstream_errors_pass_through_with_their_status()
    {
        var s = await _f.RegisterAsync();
        _f.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"message":"Not Found"}"""),
        };

        var json = await Body(await Fetch(s, Req("github", "/repos/a/b")));

        json.GetProperty("status").GetInt32().ShouldBe(404);
    }

    [Fact]
    public async Task Oversized_provider_responses_become_502()
    {
        var s = await _f.RegisterAsync();
        _f.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 2_000_000)),
        };

        (await Fetch(s, Req("github", "/big"))).StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Fetch_is_rate_limited_per_provider_per_user()
    {
        using var limited = new ServerFactory(
            new() { ["RATE_GATEWAY_FETCH_PROVIDER_PER_MIN"] = "2" }
        );
        var s = await limited.RegisterAsync();

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            var m = new HttpRequestMessage(HttpMethod.Post, "/v1/gateway/fetch")
            {
                Content = Json(Req("github", "/user")),
            };
            codes.Add((await s.Client.SendAsync(m)).StatusCode);
        }
        // A different provider has its own budget.
        var other = new HttpRequestMessage(HttpMethod.Post, "/v1/gateway/fetch")
        {
            Content = Json(Req("linear", "/graphql", "POST", body: new { query = "{ a }" })),
        };
        codes.Add((await s.Client.SendAsync(other)).StatusCode);

        codes.ShouldBe([
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.OK,
        ]);
    }

    [Fact]
    public async Task Fetch_is_rate_limited_per_user()
    {
        using var limited = new ServerFactory(new() { ["RATE_GATEWAY_FETCH_PER_MIN"] = "2" });
        var s = await limited.RegisterAsync();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            codes.Add(
                (
                    await s.Client.SendAsync(
                        new HttpRequestMessage(HttpMethod.Post, "/v1/gateway/fetch")
                        {
                            Content = Json(Req("github", "/user")),
                        }
                    )
                ).StatusCode
            );

        codes.Last().ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Tokens_and_bodies_are_not_logged()
    {
        var logs = _f.Logs;
        var factory = _f;
        var s = await factory.RegisterAsync();
        factory.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"secret_body":"TOP-SECRET-BODY"}"""),
        };

        var m = new HttpRequestMessage(HttpMethod.Post, "/v1/gateway/fetch")
        {
            Content = Json(Req("github", "/user")),
        };
        m.Headers.TryAddWithoutValidation(
            "X-Provider-Authorization",
            "Bearer ghp_SUPERSECRETTOKEN"
        );
        await s.Client.SendAsync(m);

        var all = string.Join("\n", logs.Messages);
        all.ShouldNotContain("ghp_SUPERSECRETTOKEN");
        all.ShouldNotContain("TOP-SECRET-BODY");
    }

    // --- opengraph -----------------------------------------------------------------------

    [Fact]
    public async Task Opengraph_extracts_tags_from_a_public_page()
    {
        var s = await _f.RegisterAsync();
        _f.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                <html><head><title>Fallback &amp; title</title>
                <meta property="og:title" content="Launch &quot;Noto&quot;">
                <meta property="og:description" content="A todo app">
                <meta property="og:image" content="/img/card.png">
                <meta property="og:site_name" content="Noto"></head></html>
                """,
                Encoding.UTF8,
                "text/html"
            ),
        };

        var json = await Body(
            await s.Client.PostAsync(
                "/v1/gateway/opengraph",
                Json(new { url = "https://example.com/post" })
            )
        );

        json.GetProperty("title").GetString().ShouldBe("Launch \"Noto\"");
        json.GetProperty("description").GetString().ShouldBe("A todo app");
        json.GetProperty("image").GetString().ShouldBe("https://example.com/img/card.png");
        json.GetProperty("site_name").GetString().ShouldBe("Noto");
    }

    [Fact]
    public async Task Opengraph_blocks_private_targets_and_bad_schemes()
    {
        var s = await _f.RegisterAsync();
        _f.Dns.Records["intranet.example.com"] = [IPAddress.Parse("192.168.1.10")];

        foreach (
            var url in new[]
            {
                "https://127.0.0.1/",
                "https://169.254.169.254/latest/meta-data/",
                "https://intranet.example.com/",
                "http://example.com/",
            }
        )
            (
                await s.Client.PostAsync("/v1/gateway/opengraph", Json(new { url }))
            ).StatusCode.ShouldBe(HttpStatusCode.Forbidden, url);
        foreach (var url in new[] { "file:///etc/passwd", "javascript:alert(1)", "not a url", "" })
            (
                await s.Client.PostAsync("/v1/gateway/opengraph", Json(new { url }))
            ).StatusCode.ShouldBe(HttpStatusCode.BadRequest, url);

        _f.Upstream.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void Opengraph_drops_non_http_image_urls_and_clips_long_values()
    {
        var html =
            $"""<meta property="og:title" content="{new string('t', 500)}"><meta property="og:image" content="javascript:alert(1)">""";
        var og = OpenGraphFetcher.Parse(html, new Uri("https://example.com/"));
        og.Image.ShouldBeNull();
        og.Title!.Length.ShouldBe(300);

        OpenGraphFetcher
            .Parse(
                """<meta property="og:image" content="data:image/png;base64,AAAA">""",
                new Uri("https://example.com/")
            )
            .Image.ShouldBeNull();
    }

    // --- oauth ---------------------------------------------------------------------------

    static string Challenge(string verifier) =>
        Convert
            .ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    static Dictionary<string, string> Query(string url) =>
        System
            .Web.HttpUtility.ParseQueryString(new Uri(url).Query)
            .AllKeys.ToDictionary(
                k => k!,
                k => System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)[k]!
            );

    [Fact]
    public async Task Oauth_start_builds_an_authorize_url_with_state_pkce_and_the_server_redirect()
    {
        var s = await _f.RegisterAsync();

        var json = await Body(
            await s.Client.PostAsync(
                "/v1/gateway/oauth/github/start",
                Json(new { code_challenge = Challenge("client-verifier") })
            )
        );

        var url = json.GetProperty("authorize_url").GetString()!;
        url.ShouldStartWith("https://github.com/login/oauth/authorize?");
        var q = Query(url);
        q["client_id"].ShouldBe("gh-client");
        q["redirect_uri"].ShouldBe("https://noto.test/v1/gateway/oauth/github/callback");
        q["state"].ShouldBe(json.GetProperty("state").GetString());
        q["code_challenge_method"].ShouldBe("S256");
        q.ShouldNotContainKey("client_secret");
        url.ShouldNotContain("gh-secret");
    }

    [Fact]
    public async Task Oauth_for_an_unconfigured_provider_is_404()
    {
        var s = await _f.RegisterAsync();
        var response = await s.Client.PostAsync("/v1/gateway/oauth/slack/start", Json(new { }));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Body(response)).GetProperty("code").GetString().ShouldBe("PROVIDER_NOT_CONFIGURED");
    }

    async Task<(
        ServerFactory.Session Session,
        string OneTimeCode,
        string Verifier
    )> CompleteFlowAsync(string provider = "github")
    {
        var s = await _f.RegisterAsync();
        const string verifier = "a-long-random-client-verifier-0123456789";
        var start = await Body(
            await s.Client.PostAsync(
                $"/v1/gateway/oauth/{provider}/start",
                Json(new { code_challenge = Challenge(verifier) })
            )
        );
        var state = start.GetProperty("state").GetString()!;
        _f.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"gho_abc","refresh_token":"ghr_def","expires_in":3600,"scope":"repo"}""",
                Encoding.UTF8,
                "application/json"
            ),
        };

        // The provider redirects the popup back; no bearer token is present there.
        var callback = await _f.CreateClient()
            .GetAsync($"/v1/gateway/oauth/{provider}/callback?code=provider-code&state={state}");
        callback.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await callback.Content.ReadAsStringAsync();
        var code = System
            .Text.RegularExpressions.Regex.Match(html, "\"code\":\"([^\"]+)\"")
            .Groups[1]
            .Value;
        return (s, code, verifier);
    }

    [Fact]
    public async Task Oauth_callback_exchanges_the_code_with_the_server_held_secret_and_never_exposes_tokens_in_the_page()
    {
        var (_, code, _) = await CompleteFlowAsync();

        code.ShouldNotBeNullOrEmpty();
        var exchange = _f.Upstream.Requests.Single();
        exchange.RequestUri!.ToString().ShouldBe("https://github.com/login/oauth/access_token");
        var form = _f.Upstream.Bodies.Single();
        form.ShouldContain("client_secret=gh-secret");
        form.ShouldContain("code=provider-code");
        form.ShouldContain("code_verifier="); // the server's own PKCE verifier
    }

    [Fact]
    public async Task Callback_page_uses_a_nonce_csp_and_posts_only_to_our_origin()
    {
        var s = await _f.RegisterAsync();
        var start = await Body(
            await s.Client.PostAsync("/v1/gateway/oauth/github/start", Json(new { }))
        );
        _f.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"t"}""",
                Encoding.UTF8,
                "application/json"
            ),
        };

        var response = await _f.CreateClient()
            .GetAsync(
                $"/v1/gateway/oauth/github/callback?code=c&state={start.GetProperty("state").GetString()}"
            );

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldContain("script-src 'nonce-");
        csp.ShouldNotContain("unsafe-inline");
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("postMessage");
        html.ShouldContain("\"https://noto.test\"");
        html.ShouldNotContain("gho_");
        html.ShouldNotContain("access_token");
    }

    [Fact]
    public async Task Redeem_returns_tokens_once_and_only_to_the_user_who_started_the_flow()
    {
        var (s, code, verifier) = await CompleteFlowAsync();
        var other = await _f.RegisterAsync();

        (
            await other.Client.PostAsync(
                "/v1/gateway/oauth/github/redeem",
                Json(new { one_time_code = code, code_verifier = verifier })
            )
        ).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // The failed attempt burned the code.
        (
            await s.Client.PostAsync(
                "/v1/gateway/oauth/github/redeem",
                Json(new { one_time_code = code, code_verifier = verifier })
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Redeem_succeeds_once_with_the_matching_verifier()
    {
        var (s, code, verifier) = await CompleteFlowAsync();

        var response = await s.Client.PostAsync(
            "/v1/gateway/oauth/github/redeem",
            Json(new { one_time_code = code, code_verifier = verifier })
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await Body(response);
        json.GetProperty("access_token").GetString().ShouldBe("gho_abc");
        json.GetProperty("refresh_token").GetString().ShouldBe("ghr_def");
        json.GetProperty("scopes").GetString().ShouldBe("repo");
        json.GetProperty("display_label").GetString().ShouldBe("GitHub");
        json.GetProperty("expires_at").GetDateTimeOffset().ShouldBeGreaterThan(_f.Time.GetUtcNow());

        (
            await s.Client.PostAsync(
                "/v1/gateway/oauth/github/redeem",
                Json(new { one_time_code = code, code_verifier = verifier })
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // single use
    }

    [Fact]
    public async Task Redeem_with_the_wrong_verifier_fails_and_burns_the_code()
    {
        var (s, code, verifier) = await CompleteFlowAsync();

        (
            await s.Client.PostAsync(
                "/v1/gateway/oauth/github/redeem",
                Json(new { one_time_code = code, code_verifier = "wrong" })
            )
        ).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (
            await s.Client.PostAsync(
                "/v1/gateway/oauth/github/redeem",
                Json(new { one_time_code = code, code_verifier = verifier })
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task One_time_codes_expire_after_60_seconds()
    {
        var (s, code, verifier) = await CompleteFlowAsync();

        _f.Time.Advance(TimeSpan.FromSeconds(61));

        (
            await s.Client.PostAsync(
                "/v1/gateway/oauth/github/redeem",
                Json(new { one_time_code = code, code_verifier = verifier })
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Oauth_state_is_single_use_and_unknown_state_is_rejected()
    {
        var s = await _f.RegisterAsync();
        var start = await Body(
            await s.Client.PostAsync("/v1/gateway/oauth/github/start", Json(new { }))
        );
        var state = start.GetProperty("state").GetString();
        _f.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"t"}""",
                Encoding.UTF8,
                "application/json"
            ),
        };
        var anonymous = _f.CreateClient();

        (
            await anonymous.GetAsync($"/v1/gateway/oauth/github/callback?code=c&state={state}")
        ).StatusCode.ShouldBe(HttpStatusCode.OK);
        (
            await anonymous.GetAsync($"/v1/gateway/oauth/github/callback?code=c&state={state}")
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (
            await anonymous.GetAsync("/v1/gateway/oauth/github/callback?code=c&state=forged")
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // A state issued for one provider can't complete another provider's flow.
        var gl = await Body(
            await s.Client.PostAsync("/v1/gateway/oauth/gitlab/start", Json(new { }))
        );
        (
            await anonymous.GetAsync(
                $"/v1/gateway/oauth/github/callback?code=c&state={gl.GetProperty("state").GetString()}"
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Provider_rejection_surfaces_as_502_without_leaking_the_response()
    {
        var s = await _f.RegisterAsync();
        var start = await Body(
            await s.Client.PostAsync("/v1/gateway/oauth/github/start", Json(new { }))
        );
        _f.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"error":"bad_verification_code","error_description":"INTERNAL-DETAIL"}""",
                Encoding.UTF8,
                "application/json"
            ),
        };

        var response = await _f.CreateClient()
            .GetAsync(
                $"/v1/gateway/oauth/github/callback?code=c&state={start.GetProperty("state").GetString()}"
            );

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("INTERNAL-DETAIL");
    }

    [Fact]
    public async Task Refresh_applies_the_client_secret_server_side()
    {
        var s = await _f.RegisterAsync();
        _f.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"new","refresh_token":"newr","expires_in":60}""",
                Encoding.UTF8,
                "application/json"
            ),
        };

        var json = await Body(
            await s.Client.PostAsync(
                "/v1/gateway/oauth/github/refresh",
                Json(new { refresh_token = "old-refresh" })
            )
        );

        json.GetProperty("access_token").GetString().ShouldBe("new");
        var form = _f.Upstream.Bodies.Single();
        form.ShouldContain("grant_type=refresh_token");
        form.ShouldContain("client_secret=gh-secret");
    }

    [Fact]
    public async Task Gitlab_oauth_targets_the_instance_given_by_the_client()
    {
        var s = await _f.RegisterAsync();

        var json = await Body(
            await s.Client.PostAsync(
                "/v1/gateway/oauth/gitlab/start",
                Json(new { instance_url = "https://gitlab.acme.dev" })
            )
        );

        json.GetProperty("authorize_url")
            .GetString()!
            .ShouldStartWith("https://gitlab.acme.dev/oauth/authorize?");
        (
            await s.Client.PostAsync(
                "/v1/gateway/oauth/github/start",
                Json(new { instance_url = "https://evil.example.com" })
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
