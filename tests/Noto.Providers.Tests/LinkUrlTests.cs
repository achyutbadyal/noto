using Noto.Core.Links;

namespace Noto.Providers.Tests;

public class LinkUrlTests
{
    [Theory]
    [InlineData("HTTPS://GitHub.com/Noto/App/pull/1", "https://github.com/Noto/App/pull/1")]
    [InlineData(
        "https://github.com/a/b/pull/1?utm_source=x&utm_medium=y",
        "https://github.com/a/b/pull/1"
    )]
    [InlineData("https://example.com/p?id=3&utm_campaign=z&fbclid=q", "https://example.com/p?id=3")]
    [InlineData("https://example.com/path/#section", "https://example.com/path")]
    [InlineData("https://example.com:443/x", "https://example.com/x")]
    [InlineData("https://example.com/", "https://example.com/")]
    public void Normalizes(string raw, string expected) =>
        LinkUrl.Normalize(raw).ShouldBe(expected);

    [Theory]
    [InlineData("ftp://example.com/x")]
    [InlineData("not a url")]
    [InlineData("javascript:alert(1)")]
    public void Rejects_non_http(string raw) => LinkUrl.Normalize(raw).ShouldBeNull();

    [Fact]
    public void Keeps_meaningful_query_params()
    {
        LinkUrl
            .Normalize("https://acme.slack.com/archives/C1/p170?thread_ts=1700.1&utm_x=1")
            .ShouldBe("https://acme.slack.com/archives/C1/p170?thread_ts=1700.1");
    }

    [Fact]
    public void Scan_finds_urls_in_title_and_notes_and_trims_punctuation()
    {
        var urls = LinkUrl.Scan(
            "Review https://github.com/a/b/pull/1.",
            "See (https://example.com/x), also [doc](https://example.com/doc) and https://github.com/a/b/pull/1?utm_source=n"
        );

        urls.ShouldBe([
            "https://github.com/a/b/pull/1",
            "https://example.com/x",
            "https://example.com/doc",
        ]);
    }

    [Fact]
    public void Scan_keeps_balanced_parentheses_in_urls()
    {
        LinkUrl
            .Scan("https://en.wikipedia.org/wiki/Noto_(typeface)")
            .ShouldBe(["https://en.wikipedia.org/wiki/Noto_(typeface)"]);
    }

    [Fact]
    public void Scan_handles_nulls_and_empty() =>
        LinkUrl.Scan(null, "", "no links here").ShouldBeEmpty();
}

public class UrlPatternTests
{
    [Fact]
    public void Captures_path_segments()
    {
        var p = new UrlPattern("github.com", "/{owner}/{repo}/pull/{n}*");
        p.TryMatch(new Uri("https://github.com/noto/app/pull/482/files"), out var c).ShouldBeTrue();
        (c["owner"], c["repo"], c["n"]).ShouldBe(("noto", "app", "482"));
    }

    [Fact]
    public void Captures_host_labels()
    {
        var p = new UrlPattern("{team}.slack.com", "/archives/{channel}/p{ts}");
        p.TryMatch(
                new Uri("https://acme-corp.slack.com/archives/C123/p1700000000123456"),
                out var c
            )
            .ShouldBeTrue();
        (c["team"], c["channel"], c["ts"]).ShouldBe(("acme-corp", "C123", "1700000000123456"));
    }

    [Fact]
    public void Greedy_capture_spans_nested_groups()
    {
        var p = new UrlPattern("gitlab.com", "/{path*}/-/merge_requests/{n}*");
        p.TryMatch(new Uri("https://gitlab.com/group/sub/project/-/merge_requests/7"), out var c)
            .ShouldBeTrue();
        c["path"].ShouldBe("group/sub/project");
    }

    [Fact]
    public void Host_wildcard_matches_one_label()
    {
        var p = new UrlPattern("*.atlassian.net", "/browse/{key}");
        p.TryMatch(new Uri("https://acme.atlassian.net/browse/PROJ-1"), out _).ShouldBeTrue();
        p.TryMatch(new Uri("https://evil.example.com/browse/PROJ-1"), out _).ShouldBeFalse();
    }

    [Fact]
    public void Does_not_match_other_hosts_or_paths()
    {
        var p = new UrlPattern("github.com", "/{o}/{r}/pull/{n}");
        p.TryMatch(new Uri("https://notgithub.com/a/b/pull/1"), out _).ShouldBeFalse();
        p.TryMatch(new Uri("https://github.com/a/b/issues/1"), out _).ShouldBeFalse();
    }
}
