using System.Net;
using Noto.Server.Config;
using Noto.Server.Gateway;
using Noto.Server.Middleware;

namespace Noto.Server.Tests;

public class SsrfGuardTests
{
    [Theory]
    // IPv4 private / special-use ranges
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.255")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.9")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.255")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    // IPv6
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    // IPv4 smuggled inside IPv6
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.1.1.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::7f00:1")] // NAT64 → 127.0.0.1
    [InlineData("2002:7f00:1::1")] // 6to4 → 127.0.0.1
    public void Rejects_non_public_addresses(string ip) =>
        SsrfGuard.IsPublic(IPAddress.Parse(ip)).ShouldBeFalse();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")] // just outside 172.16/12
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")] // just outside 100.64/10
    [InlineData("192.169.0.1")]
    [InlineData("198.20.0.1")]
    [InlineData("223.255.255.255")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")] // NAT64 → 8.8.8.8
    public void Accepts_public_addresses(string ip) =>
        SsrfGuard.IsPublic(IPAddress.Parse(ip)).ShouldBeTrue();
}

public class SafeFetcherTests
{
    readonly FakeDns _dns = new();
    readonly FakeUpstream _upstream = new();

    SafeFetcher Fetcher(params string[] allowPrivate) =>
        new(
            _dns,
            _upstream,
            new ServerConfig(
                new string('k', 40),
                new Uri("https://noto.test"),
                DbProvider.Sqlite,
                "",
                "",
                new HashSet<string>(allowPrivate),
                [],
                new RateLimits(),
                new Argon2Settings()
            )
        );

    static HttpResponseMessage Redirect(string to, int status = 302)
    {
        var r = new HttpResponseMessage((HttpStatusCode)status);
        r.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
        return r;
    }

    static HttpResponseMessage Ok(string body = "hi", string type = "text/html") =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, type),
        };

    Task<SafeResponse> Get(SafeFetcher f, string url, SafeFetchOptions? options = null) =>
        f.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), options, default);

    [Fact]
    public async Task Fetches_a_public_host()
    {
        _upstream.Respond = _ => Ok("hello");
        var r = await Get(Fetcher(), "https://example.com/page");
        r.Status.ShouldBe(200);
        System.Text.Encoding.UTF8.GetString(r.Body).ShouldBe("hello");
    }

    [Theory]
    [InlineData("http://example.com/")] // plain http
    [InlineData("ftp://example.com/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pw@example.com/")]
    public async Task Rejects_bad_schemes_and_credentials_in_urls(string url) =>
        await Should.ThrowAsync<ApiException>(() => Get(Fetcher(), url));

    [Theory]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://10.1.2.3/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    public async Task Rejects_private_ip_literals(string url)
    {
        var ex = await Should.ThrowAsync<ApiException>(() => Get(Fetcher(), url));
        ex.Code.ShouldBe("PRIVATE_ADDRESS");
        _upstream.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rejects_a_name_that_resolves_to_a_private_address()
    {
        _dns.Records["evil.example.com"] = [IPAddress.Parse("10.0.0.5")];
        var ex = await Should.ThrowAsync<ApiException>(() =>
            Get(Fetcher(), "https://evil.example.com/")
        );
        ex.Code.ShouldBe("PRIVATE_ADDRESS");
    }

    [Fact]
    public async Task Rejects_when_any_resolved_address_is_private()
    {
        _dns.Records["mixed.example.com"] =
        [
            IPAddress.Parse("93.184.216.34"),
            IPAddress.Parse("127.0.0.1"),
        ];
        await Should.ThrowAsync<ApiException>(() => Get(Fetcher(), "https://mixed.example.com/"));
    }

    [Fact]
    public async Task Re_validates_every_redirect_hop()
    {
        _dns.Records["internal.example.com"] = [IPAddress.Parse("192.168.0.9")];
        _upstream.Respond = req =>
            req.RequestUri!.Host == "example.com"
                ? Redirect("https://internal.example.com/admin")
                : Ok();

        var ex = await Should.ThrowAsync<ApiException>(() =>
            Get(Fetcher(), "https://example.com/")
        );

        ex.Code.ShouldBe("PRIVATE_ADDRESS");
        _upstream.Requests.Select(r => r.RequestUri!.Host).ShouldBe(["example.com"]); // never contacted the internal host
    }

    [Fact]
    public async Task Redirecting_to_a_literal_private_ip_is_blocked()
    {
        _upstream.Respond = _ => Redirect("https://127.0.0.1:8080/");
        var ex = await Should.ThrowAsync<ApiException>(() =>
            Get(Fetcher(), "https://example.com/")
        );
        ex.Code.ShouldBe("PRIVATE_ADDRESS");
    }

    [Fact]
    public async Task Redirect_downgrade_to_http_is_blocked()
    {
        _upstream.Respond = _ => Redirect("http://example.org/");
        var ex = await Should.ThrowAsync<ApiException>(() =>
            Get(Fetcher(), "https://example.com/")
        );
        ex.Code.ShouldBe("HTTP_NOT_ALLOWED");
    }

    [Fact]
    public async Task Follows_up_to_five_redirects_then_stops()
    {
        var hops = 0;
        _upstream.Respond = _ => Redirect($"https://example.com/{++hops}");

        var ex = await Should.ThrowAsync<ApiException>(() =>
            Get(Fetcher(), "https://example.com/")
        );

        ex.Code.ShouldBe("TOO_MANY_REDIRECTS");
        _upstream.Requests.Count.ShouldBe(6); // original + 5 redirects
    }

    [Fact]
    public async Task Five_redirects_are_fine()
    {
        var hops = 0;
        _upstream.Respond = _ => hops++ < 5 ? Redirect($"https://example.com/{hops}") : Ok("done");
        (await Get(Fetcher(), "https://example.com/")).Status.ShouldBe(200);
    }

    [Fact]
    public async Task Authorization_is_not_forwarded_across_hosts()
    {
        _upstream.Respond = req =>
            req.RequestUri!.Host == "api.example.com"
                ? Redirect("https://other.example.net/x")
                : Ok();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret");

        await Fetcher().SendAsync(request, null, default);

        _upstream.Requests[0].Headers.Contains("Authorization").ShouldBeTrue();
        _upstream.Requests[1].Headers.Contains("Authorization").ShouldBeFalse();
    }

    [Fact]
    public async Task Redirects_can_be_restricted_to_an_allowlist()
    {
        _upstream.Respond = _ => Redirect("https://elsewhere.example.net/");
        var ex = await Should.ThrowAsync<ApiException>(() =>
            Get(
                Fetcher(),
                "https://api.example.com/",
                new SafeFetchOptions { AllowRedirect = u => u.Host == "api.example.com" }
            )
        );
        ex.Code.ShouldBe("REDIRECT_NOT_ALLOWED");
    }

    [Fact]
    public async Task Bodies_are_capped_at_one_megabyte()
    {
        _upstream.Respond = _ => Ok(new string('a', 2_000_000));
        var r = await Get(Fetcher(), "https://example.com/");
        r.Truncated.ShouldBeTrue();
        r.Body.Length.ShouldBe(1024 * 1024);
    }

    [Fact]
    public async Task Slow_upstreams_time_out_with_502()
    {
        var gate = new TaskCompletionSource();
        var slow = new SlowHandler();
        var fetcher = new SafeFetcher(
            _dns,
            slow,
            new ServerConfig(
                new string('k', 40),
                new Uri("https://noto.test"),
                DbProvider.Sqlite,
                "",
                "",
                new HashSet<string>(),
                [],
                new RateLimits(),
                new Argon2Settings()
            )
        );

        var ex = await Should.ThrowAsync<ApiException>(() =>
            fetcher.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://example.com/"),
                new SafeFetchOptions { Timeout = TimeSpan.FromMilliseconds(100) },
                default
            )
        );

        ex.Status.ShouldBe(502);
        ex.Code.ShouldBe("PROVIDER_TIMEOUT");
        gate.TrySetResult();
    }

    [Fact]
    public async Task Operator_allowlisted_private_hosts_are_reachable_over_plain_http()
    {
        _dns.Records["jira.acme.internal"] = [IPAddress.Parse("10.2.3.4")];
        _upstream.Respond = _ => Ok("on-prem");

        var r = await Get(Fetcher("jira.acme.internal"), "http://jira.acme.internal/rest/api");

        r.Status.ShouldBe(200);
    }

    sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage();
        }
    }
}
