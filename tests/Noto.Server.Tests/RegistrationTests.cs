using System.Net;
using System.Net.Http.Json;
using Noto.Server.Config;

namespace Noto.Server.Tests;

public sealed class RegistrationTests : IDisposable
{
    const string Code = "invite-code-0123456789";

    readonly ServerFactory _closed = new(new() { ["REGISTRATION"] = "closed" });
    readonly ServerFactory _invite = new(
        new() { ["REGISTRATION"] = "invite", ["INVITE_CODES"] = $"other-code-9876543210,{Code}" }
    );

    public void Dispose()
    {
        _closed.Dispose();
        _invite.Dispose();
    }

    static object Body(string? inviteCode) =>
        new
        {
            email = $"{Guid.NewGuid():N}@example.com",
            password = "correct horse battery",
            device = new
            {
                id = Guid.NewGuid(),
                name = "Test laptop",
                platform = "macos",
            },
            invite_code = inviteCode,
        };

    [Fact]
    public async Task Closed_rejects_every_signup()
    {
        var response = await _closed
            .CreateClient()
            .PostAsJsonAsync("/v1/auth/register", Body(Code));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invite_rejects_missing_or_wrong_codes()
    {
        var client = _invite.CreateClient();

        (await client.PostAsJsonAsync("/v1/auth/register", Body(null))).StatusCode.ShouldBe(
            HttpStatusCode.Forbidden
        );
        (
            await client.PostAsJsonAsync("/v1/auth/register", Body("guessed-code-000000"))
        ).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invite_accepts_any_listed_code()
    {
        var client = _invite.CreateClient();

        (await client.PostAsJsonAsync("/v1/auth/register", Body(Code))).StatusCode.ShouldBe(
            HttpStatusCode.Created
        );
        (
            await client.PostAsJsonAsync("/v1/auth/register", Body("other-code-9876543210"))
        ).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public void Closed_is_the_default_and_invite_needs_codes()
    {
        var closed = Load(new Dictionary<string, string?>());
        closed.Registration.ShouldBe(RegistrationMode.Closed);

        Should.Throw<ServerConfigException>(() =>
            Load(new Dictionary<string, string?> { ["REGISTRATION"] = "invite" })
        );
        Should.Throw<ServerConfigException>(() =>
            Load(new Dictionary<string, string?> { ["REGISTRATION"] = "sometimes" })
        );
        Should.Throw<ServerConfigException>(() =>
            Load(
                new Dictionary<string, string?>
                {
                    ["REGISTRATION"] = "invite",
                    ["INVITE_CODES"] = "too-short",
                }
            )
        );
    }

    static ServerConfig Load(Dictionary<string, string?> overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["JWT_SIGNING_KEY"] = "test-signing-key-that-is-long-enough-for-hs256!",
            ["PUBLIC_URL"] = "https://noto.test",
        };
        foreach (var (k, v) in overrides)
            settings[k] = v;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return ServerConfig.Load(config);
    }
}
