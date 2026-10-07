using Noto.Core.Links;
using Noto.Providers.Providers;

namespace Noto.Providers.Tests;

public class RegistryTests
{
    static ProviderRegistry Registry() => new([
        new GitHubProvider(), new JiraProvider(), new LinearProvider(), new SlackProvider(), new GitLabProvider(),
        new NotionProvider(), new ConfluenceProvider(), new FigmaProvider(), new OpenGraphProvider(),
    ]);

    static AppConnection Conn(string provider, string? instance = null) => new()
    {
        Id = Guid.CreateVersion7(), ProviderId = provider, InstanceUrl = instance, DisplayLabel = "x",
        AuthMethod = AuthMethod.PersonalToken, ConnectedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void Matches_fixed_host_provider_with_its_connection()
    {
        var gh = Conn("github");
        var match = Registry().Match(new Uri("https://github.com/a/b/pull/1"), [gh])!;
        (match.Provider.ProviderId, match.Connection).ShouldBe(("github", gh));
        match.Hint.ShouldBeNull();
    }

    [Fact]
    public void Without_a_connection_it_is_recognized_with_a_connect_hint()
    {
        var match = Registry().Match(new Uri("https://github.com/a/b/pull/1"), [])!;
        match.Provider.ProviderId.ShouldBe("github");
        match.Connection.ShouldBeNull();
        match.Hint!.ShouldContain("Connect GitHub");
    }

    [Fact]
    public void Host_picks_the_slack_connection()
    {
        var acme = Conn("slack", "https://acme-corp.slack.com");
        var other = Conn("slack", "https://other.slack.com");

        var match = Registry().Match(new Uri("https://other.slack.com/archives/C1/p1700000000123456"), [acme, other])!;

        match.Connection.ShouldBe(other);
    }

    [Fact]
    public void Self_hosted_jira_matches_by_instance_host()
    {
        var jira = Conn("jira", "https://jira.acme.com");
        Registry().Match(new Uri("https://jira.acme.com/browse/PROJ-1"), [jira])!.Connection.ShouldBe(jira);
    }

    [Fact]
    public void Unknown_jira_host_gets_a_connect_hint_naming_the_host()
    {
        var match = Registry().Match(new Uri("https://jira.acme.com/browse/PROJ-1"), [])!;
        match.Provider.ProviderId.ShouldBe("jira");
        match.Connection.ShouldBeNull();
        match.Hint.ShouldBe("Connect Jira (jira.acme.com)");
    }

    [Fact]
    public void Github_enterprise_matches_through_its_connection()
    {
        var ghes = Conn("github", "https://ghe.acme.com");
        Registry().Match(new Uri("https://ghe.acme.com/a/b/pull/3"), [ghes])!.Connection.ShouldBe(ghes);
    }

    [Fact]
    public void Connection_for_a_different_host_does_not_capture_the_url()
    {
        var ghes = Conn("github", "https://ghe.acme.com");
        Registry().Match(new Uri("https://github.com/a/b/pull/3"), [ghes])!.Connection.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://linear.app/acme/issue/ENG-1/title", "linear")]
    [InlineData("https://acme.atlassian.net/wiki/spaces/ENG/pages/123/Title", "confluence")]
    [InlineData("https://www.figma.com/design/abc123/Name", "figma")]
    [InlineData("https://www.notion.so/Roadmap-0123456789abcdef0123456789abcdef", "notion")]
    [InlineData("https://gitlab.com/g/p/-/merge_requests/2", "gitlab")]
    public void Recognizes_other_providers(string url, string expected) =>
        Registry().Match(new Uri(url), [])!.Provider.ProviderId.ShouldBe(expected);

    [Fact]
    public void Everything_else_falls_back_to_opengraph()
    {
        var match = Registry().Match(new Uri("https://blog.example.com/post"), [])!;
        match.Provider.ProviderId.ShouldBe("opengraph");
        match.Hint.ShouldBeNull();
    }
}
