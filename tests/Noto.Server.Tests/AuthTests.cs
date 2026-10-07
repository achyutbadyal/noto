using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Noto.Server.Config;

namespace Noto.Server.Tests;

public sealed class AuthTests : IDisposable
{
    readonly ServerFactory _f = new();
    public void Dispose() => _f.Dispose();

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Register_returns_201_with_tokens_and_the_device_is_listed()
    {
        var s = await _f.RegisterAsync();

        var devices = await s.Client.GetFromJsonAsync<JsonElement>("/v1/devices");

        devices.GetArrayLength().ShouldBe(1);
        devices[0].GetProperty("id").GetGuid().ShouldBe(s.DeviceId);
        devices[0].GetProperty("platform").GetString().ShouldBe("macos");
        devices[0].GetProperty("cursor").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task Duplicate_email_conflicts_with_a_problem_document()
    {
        await _f.RegisterAsync("dupe@example.com");

        var response = await _f.CreateClient().PostAsJsonAsync("/v1/auth/register",
            new { email = "DUPE@example.com", password = "correct horse battery", device = ServerFactory.Device(Guid.NewGuid()) });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await Json(response)).GetProperty("code").GetString().ShouldBe("EMAIL_TAKEN");
    }

    [Theory]
    [InlineData("not-an-email", "correct horse battery", "INVALID_EMAIL")]
    [InlineData("a@example.com", "short", "WEAK_PASSWORD")]
    public async Task Register_validates_input(string email, string password, string code)
    {
        var response = await _f.CreateClient().PostAsJsonAsync("/v1/auth/register",
            new { email, password, device = ServerFactory.Device(Guid.NewGuid()) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Login_works_and_wrong_credentials_are_indistinguishable()
    {
        var s = await _f.RegisterAsync("me@example.com");
        var client = _f.CreateClient();

        var ok = await client.PostAsJsonAsync("/v1/auth/login", new { email = "me@example.com", password = "correct horse battery", device = ServerFactory.Device(Guid.NewGuid()) });
        var badPassword = await client.PostAsJsonAsync("/v1/auth/login", new { email = "me@example.com", password = "wrong password!!", device = ServerFactory.Device(Guid.NewGuid()) });
        var unknown = await client.PostAsJsonAsync("/v1/auth/login", new { email = "nobody@example.com", password = "correct horse battery", device = ServerFactory.Device(Guid.NewGuid()) });

        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        badPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await badPassword.Content.ReadAsStringAsync()).ShouldBe(await unknown.Content.ReadAsStringAsync());
        (await s.Client.GetFromJsonAsync<JsonElement>("/v1/devices")).GetArrayLength().ShouldBe(2); // login registered a device
    }

    [Fact]
    public async Task Protected_endpoints_require_a_valid_token()
    {
        var anonymous = _f.CreateClient();
        (await anonymous.GetAsync("/v1/devices")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "garbage");
        (await anonymous.GetAsync("/v1/devices")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Access_tokens_expire_after_15_minutes()
    {
        var s = await _f.RegisterAsync();
        (await s.Client.GetAsync("/v1/devices")).StatusCode.ShouldBe(HttpStatusCode.OK);

        _f.Time.Advance(TimeSpan.FromMinutes(14));
        (await s.Client.GetAsync("/v1/devices")).StatusCode.ShouldBe(HttpStatusCode.OK);

        _f.Time.Advance(TimeSpan.FromMinutes(2));
        (await s.Client.GetAsync("/v1/devices")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_rotates_tokens_and_the_new_pair_works()
    {
        var s = await _f.RegisterAsync();

        var response = await _f.CreateClient().PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = s.RefreshToken });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pair = await Json(response);

        pair.GetProperty("refresh_token").GetString().ShouldNotBe(s.RefreshToken);
        var client = _f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pair.GetProperty("access_token").GetString());
        (await client.GetAsync("/v1/devices")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reusing_a_refresh_token_revokes_the_device()
    {
        var s = await _f.RegisterAsync();
        var client = _f.CreateClient();
        var first = await Json(await client.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = s.RefreshToken }));

        var reuse = await client.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = s.RefreshToken });

        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Json(reuse)).GetProperty("code").GetString().ShouldBe("REFRESH_TOKEN_REUSED");

        // The legitimately rotated token and the access token are dead too: the whole device is revoked.
        (await client.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = first.GetProperty("refresh_token").GetString() })).StatusCode
            .ShouldNotBe(HttpStatusCode.OK);
        (await s.Client.GetAsync("/v1/devices")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Refresh_tokens_expire()
    {
        var s = await _f.RegisterAsync();
        _f.Time.Advance(TimeSpan.FromDays(91));

        (await _f.CreateClient().PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = s.RefreshToken })).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_devices_refresh_token()
    {
        var s = await _f.RegisterAsync();

        (await s.Client.PostAsync("/v1/auth/logout", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _f.CreateClient().PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = s.RefreshToken })).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_revoked_device_is_refused_immediately_even_with_an_unexpired_token()
    {
        var laptop = await _f.RegisterAsync("two@example.com");
        var phoneId = Guid.NewGuid();
        var phoneLogin = await _f.CreateClient().PostAsJsonAsync("/v1/auth/login", new { email = "two@example.com", password = "correct horse battery", device = ServerFactory.Device(phoneId) });
        var phone = _f.CreateClient();
        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(phoneLogin)).GetProperty("access_token").GetString());

        (await laptop.Client.DeleteAsync($"/v1/devices/{phoneId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await phone.GetAsync("/v1/devices");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Json(response)).GetProperty("code").GetString().ShouldBe("DEVICE_REVOKED");
        (await laptop.Client.GetFromJsonAsync<JsonElement>("/v1/devices")).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Users_cannot_revoke_each_others_devices()
    {
        var a = await _f.RegisterAsync();
        var b = await _f.RegisterAsync();

        (await b.Client.DeleteAsync($"/v1/devices/{a.DeviceId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_device_id_cannot_be_claimed_by_a_second_account()
    {
        var a = await _f.RegisterAsync();

        var response = await _f.CreateClient().PostAsJsonAsync("/v1/auth/register",
            new { email = "other@example.com", password = "correct horse battery", device = ServerFactory.Device(a.DeviceId) });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Passwords_are_stored_as_argon2id_hashes()
    {
        await _f.RegisterAsync("hash@example.com");
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.ServerDbContext>();

        var stored = db.Users.Single(u => u.Email == "hash@example.com").PasswordHash;

        stored.ShouldStartWith("argon2id$");
        stored.ShouldNotContain("correct horse");
    }

    [Fact]
    public async Task Refresh_tokens_are_stored_hashed()
    {
        var s = await _f.RegisterAsync();
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.ServerDbContext>();

        db.RefreshTokens.Any(t => t.TokenHash == s.RefreshToken).ShouldBeFalse();
    }

    [Fact]
    public async Task Auth_endpoints_are_rate_limited_per_ip_with_retry_after()
    {
        using var limited = new ServerFactory(new() { ["RATE_AUTH_PER_MIN"] = "3" });
        var client = limited.CreateClient();
        HttpResponseMessage? last = null;

        for (var i = 0; i < 5; i++)
            last = await client.PostAsJsonAsync("/v1/auth/login", new { email = "x@example.com", password = "whatever12345", device = ServerFactory.Device(Guid.NewGuid()) });

        last!.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Json(last)).GetProperty("code").GetString().ShouldBe("RATE_LIMITED");
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_a_strict_csp()
    {
        var response = await _f.CreateClient().GetAsync("/v1/health");

        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        response.Headers.GetValues("Referrer-Policy").ShouldContain("no-referrer");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldContain("default-src 'self'");
        csp.ShouldContain("frame-ancestors 'none'");
        csp.ShouldNotContain(" 'unsafe-eval'");
        csp.ShouldNotContain("script-src 'self' 'unsafe-inline'");
    }
}

public class ConfigTests
{
    static IConfiguration Config(params (string, string?)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.ToDictionary(p => p.Item1, p => p.Item2)).Build();

    [Fact]
    public void Refuses_to_start_without_the_required_secrets()
    {
        var ex = Should.Throw<ServerConfigException>(() => ServerConfig.Load(Config()));
        ex.Message.ShouldContain("JWT_SIGNING_KEY");
        ex.Message.ShouldContain("PUBLIC_URL");
    }

    [Fact]
    public void Rejects_a_short_signing_key()
    {
        Should.Throw<ServerConfigException>(() => ServerConfig.Load(Config(("JWT_SIGNING_KEY", "short"), ("PUBLIC_URL", "https://x.test"))));
    }

    [Fact]
    public void Postgres_requires_a_database_url_and_converts_it()
    {
        Should.Throw<ServerConfigException>(() => ServerConfig.Load(Config(("JWT_SIGNING_KEY", new string('k', 40)), ("PUBLIC_URL", "https://x.test"), ("DB", "postgres"))));

        var cfg = ServerConfig.Load(Config(("JWT_SIGNING_KEY", new string('k', 40)), ("PUBLIC_URL", "https://x.test"),
            ("DATABASE_URL", "postgresql://noto:p%40ss@db:5432/noto")));

        cfg.Db.ShouldBe(DbProvider.Postgres);
        cfg.ConnectionString.ShouldBe("Host=db;Port=5432;Database=noto;Username=noto;Password=p@ss");
    }

    [Fact]
    public void Defaults_to_sqlite_in_the_data_dir_and_parses_allowlists()
    {
        var cfg = ServerConfig.Load(Config(("JWT_SIGNING_KEY", new string('k', 40)), ("PUBLIC_URL", "https://x.test"),
            ("DATA_DIR", "/data"), ("GATEWAY_ALLOW_PRIVATE_HOSTS", "jira.acme.internal, wiki.acme.internal")));

        cfg.Db.ShouldBe(DbProvider.Sqlite);
        cfg.ConnectionString.ShouldBe("Data Source=/data/noto.db");
        cfg.AllowPrivateHosts.ShouldBe(["jira.acme.internal", "wiki.acme.internal"], ignoreOrder: true);
    }

    [Fact]
    public void The_host_refuses_to_build_without_secrets()
    {
        Should.Throw<ServerConfigException>(() =>
        {
            foreach (var k in new[] { "JWT_SIGNING_KEY", "PUBLIC_URL" }) Environment.SetEnvironmentVariable(k, null);
            ServerHost.Build([]);
        });
    }
}
