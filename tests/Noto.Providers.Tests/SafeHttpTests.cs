using System.Net;
using Noto.Providers.Transport;

namespace Noto.Providers.Tests;

public sealed class SafeHttpTests
{
    static HttpResponseMessage Redirect(string to) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(to) } };

    [Fact]
    public async Task A_redirect_to_another_host_is_not_followed_so_credentials_stay_put()
    {
        var fake = new FakeHandler((_, _) => Redirect("https://evil.example/steal"));
        using var client = new HttpClient(new SameOriginRedirectHandler(fake));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/x");
        request.Headers.TryAddWithoutValidation("X-Api-Key", "secret");

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        fake.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_redirect_within_the_same_origin_is_followed_with_its_headers()
    {
        var fake = new FakeHandler(
            (r, _) =>
                r.RequestUri!.AbsolutePath == "/old"
                    ? Redirect("https://api.example/new")
                    : FakeHandler.Json("{}")
        );
        using var client = new HttpClient(new SameOriginRedirectHandler(fake));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/old");
        request.Headers.TryAddWithoutValidation("X-Api-Key", "secret");

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        fake.Calls[1].Request.Headers.GetValues("X-Api-Key").ShouldBe(["secret"]);
    }

    [Theory]
    [InlineData("https://api.example", true)]
    [InlineData("http://localhost:8080", true)]
    [InlineData("http://127.0.0.1:1", true)]
    [InlineData("http://api.example", false)]
    public void Secrets_need_https_unless_the_target_is_this_machine(string url, bool ok) =>
        SafeHttp.IsSecureOrLoopback(new Uri(url)).ShouldBe(ok);

    [Fact]
    public async Task Link_previews_refuse_a_loopback_target()
    {
        using var client = new HttpClient(SafeHttp.PreviewHandler());

        await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync("http://127.0.0.1:9/"));
    }
}
