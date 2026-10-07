using Noto.Core.Links;
using Noto.Providers.Auth;
using Noto.Providers.CustomApp;
using Noto.Providers.Providers;
using Noto.Providers.Transport;

namespace Noto.Providers.Tests;

public class GitHubProviderTests
{
    readonly GitHubProvider _github = new();

    static Uri U(string s) => new(s);

    [Fact]
    public async Task Batch_maps_pr_issue_and_missing_objects_in_order()
    {
        var http = new FakeHttp().OnPath("graphql", Fixture.Load("github_batch.json"));
        var urls = new[]
        {
            U("https://github.com/noto/noto-app/pull/482"), U("https://github.com/noto/noto-app/pull/483"),
            U("https://github.com/noto/noto-app/issues/12"), U("https://github.com/noto/noto-app/pull/999"),
        };

        var previews = await _github.FetchBatchAsync(urls, http, default);

        http.Requests.Count.ShouldBe(1); // one GraphQL round trip for four objects
        var pr = previews[0];
        pr.Title.ShouldBe("Add rich preview support");
        pr.Subtitle.ShouldBe("noto/noto-app #482");
        pr.State.ShouldBe(LinkState.Open);
        pr.ChipFacts.Select(f => f.Text).ShouldBe(["2 approved", "CI passed", "ready"]);
        pr.AuthorName.ShouldBe("achyut");
        pr.Metadata["kind"].GetString().ShouldBe("pr");

        previews[1].State.ShouldBe(LinkState.Done);
        previews[1].ChipFacts.Last().Text.ShouldBe("merged");

        previews[2].State.ShouldBe(LinkState.Open);
        previews[2].ChipFacts.Select(f => f.Text).ShouldBe(["open", "@achyut", "4 comments"]);

        previews[3].Status.ShouldBe(PreviewStatus.Unavailable);
    }

    [Fact]
    public async Task Batches_up_to_fifty_objects_per_query()
    {
        var http = new FakeHttp().OnPath("graphql", """{ "data": {} }""");
        var urls = Enumerable.Range(1, 120).Select(n => U($"https://github.com/a/b/pull/{n}")).ToList();

        var previews = await _github.FetchBatchAsync(urls, http, default);

        previews.Count.ShouldBe(120);
        http.Requests.Count.ShouldBe(3); // 50 + 50 + 20
        http.Requests.ShouldAllBe(r => r.Method == "POST");
    }

    [Fact]
    public async Task Review_state_drives_normalized_state_and_the_hash_changes_with_ci()
    {
        var open = (await _github.FetchAsync(U("https://github.com/noto/noto-app/pull/482"),
            new FakeHttp().OnPath("graphql", Fixture.Load("github_pr_open.json")), default));
        var merged = (await _github.FetchAsync(U("https://github.com/noto/noto-app/pull/482"),
            new FakeHttp().OnPath("graphql", Fixture.Load("github_pr_merged.json")), default));

        open.State.ShouldBe(LinkState.InReview); // REVIEW_REQUIRED
        open.ChipFacts.Select(f => f.Text).ShouldContain("CI pending");
        merged.State.ShouldBe(LinkState.Done);
        merged.StateHash.ShouldNotBe(open.StateHash);
    }

    [Fact]
    public async Task Hostile_repo_names_never_reach_the_query()
    {
        var http = new FakeHttp().OnPath("graphql", """{ "data": {} }""");
        var evil = U("https://github.com/a%22%20mutation/b/pull/1");

        var previews = await _github.FetchBatchAsync([evil], http, default);

        previews[0].Status.ShouldBe(PreviewStatus.Unavailable);
        http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enterprise_validate_falls_back_to_the_v3_path()
    {
        var http = new FakeHttp().OnPath("v3/user", """{ "login": "dana" }""");
        (await _github.ValidateAsync(http, default)).DisplayLabel.ShouldBe("dana");
        http.Requests.Select(r => r.Path).ShouldBe(["user", "v3/user"]);
    }

    [Fact]
    public void Api_root_differs_for_enterprise()
    {
        _github.ApiRoot(null).ToString().ShouldBe("https://api.github.com/");
        _github.ApiRoot("https://ghe.acme.com").ToString().ShouldBe("https://ghe.acme.com/api/");
    }

    [Fact]
    public async Task Unauthorized_surfaces_as_an_exception_for_the_service_to_map()
    {
        var http = new FakeHttp().OnPath("graphql", "{}", status: 401);
        var ex = await Should.ThrowAsync<ProviderHttpException>(() => _github.FetchAsync(U("https://github.com/a/b/pull/1"), http, default));
        ex.Status.ShouldBe(401);
    }
}

public class JiraProviderTests
{
    readonly JiraProvider _jira = new();

    [Fact]
    public async Task Batch_uses_one_jql_search_and_maps_states()
    {
        var http = new FakeHttp().OnPath("rest/api/3/search/jql", Fixture.Load("jira_search.json"));
        var urls = new[] { "PROJ-1", "PROJ-2", "PROJ-3", "PROJ-9" }.Select(k => new Uri($"https://acme.atlassian.net/browse/{k}")).ToList();

        var previews = await _jira.FetchBatchAsync(urls, http, default);

        http.Requests.Count.ShouldBe(1);
        http.Requests[0].Query!["jql"].ShouldBe("key in (PROJ-1,PROJ-2,PROJ-3,PROJ-9)");
        previews.Select(p => p.State).ShouldBe([LinkState.InProgress, LinkState.Blocked, LinkState.Done, null]);
        previews[0].ChipFacts.Select(f => f.Text).ShouldBe(["In Progress", "@Dana"]);
        previews[0].Subtitle.ShouldBe("PROJ-1");
        previews[3].Status.ShouldBe(PreviewStatus.Unavailable);
    }

    [Fact]
    public async Task Hash_changes_when_status_name_moves_within_a_category()
    {
        var url = new Uri("https://acme.atlassian.net/browse/PROJ-2");
        var blocked = await _jira.FetchAsync(url, new FakeHttp().OnPath("rest/api/3/search/jql", Fixture.Load("jira_search_blocked.json")), default);
        var moving = await _jira.FetchAsync(url, new FakeHttp().OnPath("rest/api/3/search/jql", Fixture.Load("jira_search_unblocked.json")), default);

        (blocked.State, moving.State).ShouldBe((LinkState.Blocked, LinkState.InProgress));
        blocked.StateHash.ShouldNotBe(moving.StateHash);
    }

    [Fact]
    public async Task Data_center_uses_rest_v2()
    {
        var http = new FakeHttp().OnPath("rest/api/2/search", Fixture.Load("jira_search.json"));
        await _jira.FetchAsync(new Uri("https://jira.acme.com/browse/PROJ-1"), http, default);
        http.Requests.Single().Path.ShouldBe("rest/api/2/search");
    }

    [Fact]
    public void Cloud_api_token_is_basic_auth_and_data_center_pat_is_bearer()
    {
        var cred = new Credential("tok", Username: "me@acme.com");
        _jira.AuthHeaders(cred, AuthMethod.ApiKey).Single().Value.ShouldBe("Basic " + Convert.ToBase64String("me@acme.com:tok"u8.ToArray()));
        _jira.AuthHeaders(cred, AuthMethod.PersonalToken).Single().Value.ShouldBe("Bearer tok");
    }
}

public class LinearProviderTests
{
    readonly LinearProvider _linear = new();

    [Fact]
    public async Task Batch_maps_state_types_and_missing_issues()
    {
        var http = new FakeHttp().OnPath("graphql", Fixture.Load("linear_batch.json"));
        var urls = new[] { "ENG-12/retry-failed-syncs", "ENG-13", "ENG-14" }.Select(x => new Uri($"https://linear.app/acme/issue/{x}")).ToList();

        var previews = await _linear.FetchBatchAsync(urls, http, default);

        http.Requests.Count.ShouldBe(1);
        previews[0].State.ShouldBe(LinkState.InReview);
        previews[0].ChipFacts.Select(f => f.Text).ShouldBe(["In Review", "@Dana", "cycle 7"]);
        previews[1].State.ShouldBe(LinkState.Done);
        previews[2].Status.ShouldBe(PreviewStatus.Unavailable);
    }

    [Fact]
    public void Personal_api_key_is_sent_without_bearer_but_oauth_is_bearer()
    {
        var cred = new Credential("lin_api_key");
        _linear.AuthHeaders(cred, AuthMethod.ApiKey).Single().Value.ShouldBe("lin_api_key");
        _linear.AuthHeaders(cred, AuthMethod.OAuth2).Single().Value.ShouldBe("Bearer lin_api_key");
    }

    [Fact]
    public async Task Rejects_ids_that_are_not_issue_identifiers()
    {
        var http = new FakeHttp();
        var previews = await _linear.FetchBatchAsync([new Uri("https://linear.app/acme/issue/not-valid-id!")], http, default);
        previews[0].Status.ShouldBe(PreviewStatus.Unavailable);
        http.Requests.ShouldBeEmpty();
    }
}

public class SlackProviderTests
{
    static FakeHttp Http() => new FakeHttp()
        .OnPath("conversations.replies", Fixture.Load("slack_replies.json"))
        .OnPath("conversations.info", Fixture.Load("slack_info.json"))
        .OnPath("users.info", Fixture.Load("slack_user.json"));

    [Fact]
    public async Task Thread_preview_uses_replies_not_history()
    {
        var http = Http();
        var p = await new SlackProvider().FetchAsync(new Uri("https://acme-corp.slack.com/archives/C123/p1700000000123456"), http, default);

        http.Requests.Select(r => r.Path).ShouldNotContain("conversations.history");
        http.Requests[0].Query!["ts"].ShouldBe("1700000000.123456");
        p.Subtitle.ShouldBe("#design-reviews");
        p.ChipFacts.Select(f => f.Text).ShouldBe(["#design-reviews", "2 replies"]);
        p.AuthorName.ShouldBe("Dana Whitfield");
        p.Metadata["latest_reply_ts"].GetString().ShouldBe("1700000200.000200");
    }

    [Fact]
    public async Task Thread_ts_query_param_wins_over_the_message_ts()
    {
        var http = Http();
        await new SlackProvider().FetchAsync(new Uri("https://acme-corp.slack.com/archives/C123/p1700000100000100?thread_ts=1700000000.123456"), http, default);
        http.Requests[0].Query!["ts"].ShouldBe("1700000000.123456");
    }

    [Fact]
    public async Task Hash_follows_reply_count_and_latest_reply()
    {
        var url = new Uri("https://acme-corp.slack.com/archives/C123/p1700000000123456");
        var more = Fixture.Load("slack_replies.json").Replace("\"Thanks\" }", "\"Thanks\" }, { \"ts\": \"1700000300.000300\", \"user\": \"U2\", \"text\": \"ok\" }");

        var before = await new SlackProvider().FetchAsync(url, Http(), default);
        var same = await new SlackProvider().FetchAsync(url, Http(), default);
        var after = await new SlackProvider().FetchAsync(url, new FakeHttp().OnPath("conversations.replies", more), default);

        same.StateHash.ShouldBe(before.StateHash);
        after.StateHash.ShouldNotBe(before.StateHash);
    }

    [Fact]
    public async Task There_is_no_resolved_state_unless_the_check_reaction_option_is_on()
    {
        var url = new Uri("https://acme-corp.slack.com/archives/C123/p1700000000123456");
        (await new SlackProvider().FetchAsync(url, Http(), default)).State.ShouldBe(LinkState.Unknown);
        (await new SlackProvider(new SlackOptions(TreatCheckReactionAsResolved: true)).FetchAsync(url, Http(), default)).State.ShouldBe(LinkState.Done);
    }

    [Fact]
    public async Task Admin_approval_is_detected_and_offers_a_request_message()
    {
        var http = new FakeHttp().OnPath("conversations.replies", """{ "ok": false, "error": "admin_approval_required" }""");
        var ex = await Should.ThrowAsync<ApprovalRequiredException>(() =>
            new SlackProvider().FetchAsync(new Uri("https://acme-corp.slack.com/archives/C123/p1700000000123456"), http, default));

        ex.RequestMessage.ShouldContain("acme-corp");
        ex.RequestMessage.ShouldContain("approve");
    }

    [Theory]
    [InlineData("channel_not_found", 404)]
    [InlineData("invalid_auth", 401)]
    [InlineData("not_in_channel", 403)]
    [InlineData("ratelimited", 429)]
    public async Task Error_codes_map_to_http_like_statuses(string error, int status)
    {
        var http = new FakeHttp().OnPath("conversations.replies", $$"""{ "ok": false, "error": "{{error}}" }""");
        var ex = await Should.ThrowAsync<ProviderHttpException>(() =>
            new SlackProvider().FetchAsync(new Uri("https://acme-corp.slack.com/archives/C123/p1700000000123456"), http, default));
        ex.Status.ShouldBe(status);
    }

    [Fact]
    public void Slack_uses_user_scope_on_the_authorize_url()
    {
        var config = new SlackProvider().GetAuthConfig();
        var url = OAuthFlow.BuildAuthorizeUrl(config, new OAuthClient("cid"), "http://127.0.0.1:1/callback", "st", "ch").ToString();
        url.ShouldContain("user_scope=");
        url.ShouldNotContain("code_challenge"); // Slack's v2 flow is not PKCE
    }
}

public class OtherProviderTests
{
    [Fact]
    public async Task GitLab_merge_request_with_nested_groups_and_approvals()
    {
        var http = new FakeHttp()
            .On(r => r.Path.EndsWith("/approvals"), Fixture.Load("gitlab_approvals.json"))
            .OnPath("projects/", Fixture.Load("gitlab_mr.json"));

        var p = await new GitLabProvider().FetchAsync(new Uri("https://gitlab.com/group/sub/project/-/merge_requests/7"), http, default);

        http.Requests[0].Path.ShouldBe("projects/group%2Fsub%2Fproject/merge_requests/7");
        p.State.ShouldBe(LinkState.InReview);
        p.ChipFacts.Select(f => f.Text).ShouldBe(["1 approved", "CI passed", "open"]);
        p.Subtitle.ShouldBe("group/sub/project!7");
    }

    [Fact]
    public async Task GitLab_closed_issue()
    {
        var http = new FakeHttp().OnPath("projects/", Fixture.Load("gitlab_issue.json"));
        var p = await new GitLabProvider().FetchAsync(new Uri("https://gitlab.com/g/p/-/issues/9"), http, default);
        (p.State, p.AuthorName).ShouldBe((LinkState.Closed, "Sam"));
    }

    [Fact]
    public void GitLab_pat_uses_private_token_header()
    {
        new GitLabProvider().AuthHeaders(new Credential("t"), AuthMethod.PersonalToken).Single().ShouldBe(new("PRIVATE-TOKEN", "t"));
    }

    [Fact]
    public async Task Notion_page_title_and_last_edited()
    {
        var http = new FakeHttp().OnPath("v1/pages/0123456789abcdef0123456789abcdef", Fixture.Load("notion_page.json"));
        var p = await new NotionProvider().FetchAsync(new Uri("https://www.notion.so/acme/Q4-Roadmap-0123456789abcdef0123456789abcdef"), http, default);

        p.Title.ShouldBe("Q4 Roadmap");
        p.ChipFacts.Single().Text.ShouldBe("edited 2026-10-01");
        p.StateHash.ShouldNotBeNull();
        http.Requests[0].Headers!["Notion-Version"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Notion_unshared_page_shows_the_share_hint()
    {
        var http = new FakeHttp().OnPath("v1/pages/", "{}", status: 404);
        var p = await new NotionProvider().FetchAsync(new Uri("https://www.notion.so/Hidden-0123456789abcdef0123456789abcdef"), http, default);

        p.Status.ShouldBe(PreviewStatus.Unavailable);
        p.ErrorMessage.ShouldBe(NotionProvider.ShareHint);
        p.Metadata["hint"].GetString().ShouldBe("share_with_integration");
    }

    [Fact]
    public async Task Confluence_page_hashes_the_version_number()
    {
        var http = new FakeHttp().OnPath("wiki/api/v2/pages/98765", Fixture.Load("confluence_page.json"));
        var p = await new ConfluenceProvider().FetchAsync(new Uri("https://acme.atlassian.net/wiki/spaces/ENG/pages/98765/Runbook"), http, default);

        p.Title.ShouldBe("Runbook: Deploys");
        p.Subtitle.ShouldBe("ENG");
        p.ChipFacts.Select(f => f.Text).ShouldBe(["ENG", "edited 2026-09-30"]);
    }

    [Fact]
    public async Task Figma_file_and_pat_header()
    {
        var http = new FakeHttp().OnPath("v1/files/abc123", Fixture.Load("figma_file.json"));
        var p = await new FigmaProvider().FetchAsync(new Uri("https://www.figma.com/design/abc123/Mobile-redesign?node-id=1"), http, default);

        p.Title.ShouldBe("Mobile redesign");
        p.Subtitle.ShouldBe("Onboarding");
        new FigmaProvider().AuthHeaders(new Credential("t"), AuthMethod.PersonalToken).Single().Key.ShouldBe("X-Figma-Token");
    }

    [Fact]
    public async Task OpenGraph_provider_has_no_live_state()
    {
        var http = new FakeHttp { OpenGraph = new OpenGraphData("A post", "About things", "https://img/x.png", "Example Blog") };
        var p = await new OpenGraphProvider().FetchAsync(new Uri("https://blog.example.com/post"), http, default);

        p.Title.ShouldBe("A post");
        p.Subtitle.ShouldBe("Example Blog");
        p.StateHash.ShouldBeNull();
        p.State.ShouldBeNull();
    }

    [Fact]
    public void OpenGraph_parser_decodes_entities_and_falls_back_to_title_tag()
    {
        var og = OpenGraphParser.Parse("""
            <html><head><title>Plain &amp; Simple</title>
            <meta property="og:description" content="It&#39;s a &quot;thing&quot;">
            <meta content="https://img/y.png" property="og:image"></head></html>
            """);

        og.Title.ShouldBe("Plain & Simple");
        og.Description.ShouldBe("It's a \"thing\"");
        og.Image.ShouldBe("https://img/y.png");
    }
}

public class CustomAppTests
{
    static CustomAppDefinition Def(CustomPreviewMode mode = CustomPreviewMode.JsonApi, CustomAuthKind auth = CustomAuthKind.Bearer) => new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"), "Internal Wiki", "wiki.acme.com/pages/{id}", auth, "api_key", mode,
        "https://wiki.acme.com/api/v1/pages/{id}", "$.title", "$.excerpt", "$.status", "archived");

    [Theory]
    [InlineData("$.a.b", "x")]
    [InlineData("$.list[1].name", "second")]
    [InlineData("$['odd-key']", "ok")]
    [InlineData("$.n", "3")]
    public void JsonPath_selects(string path, string expected)
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            { "a": { "b": "x" }, "list": [ { "name": "first" }, { "name": "second" } ], "odd-key": "ok", "n": 3 }
            """);
        JsonPathSelector.SelectString(doc.RootElement, path).ShouldBe(expected);
    }

    [Theory]
    [InlineData("$.missing")]
    [InlineData("$.list[9]")]
    [InlineData("a.b")]
    [InlineData("")]
    public void JsonPath_returns_null_when_absent_or_malformed(string path)
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{ "list": [1] }""");
        JsonPathSelector.SelectString(doc.RootElement, path).ShouldBeNull();
    }

    [Fact]
    public async Task Json_api_mapping_with_done_when_makes_a_live_link()
    {
        var provider = new CustomAppProvider(Def());
        var http = new FakeHttp().OnPath("https://wiki.acme.com/api/v1/pages/42", Fixture.Load("custom_page.json"));

        var p = await provider.FetchAsync(new Uri("https://wiki.acme.com/pages/42"), http, default);

        p.Title.ShouldBe("Onboarding Guide");
        p.Snippet.ShouldBe("How new hires get set up");
        p.State.ShouldBe(LinkState.Done); // status == archived
        p.StateHash.ShouldNotBeNull();
        p.ProviderId.ShouldBe("custom:11111111222233334444555555555555");
    }

    [Fact]
    public async Task Without_a_state_mapping_there_is_no_hash()
    {
        var provider = new CustomAppProvider(Def() with { StatePath = null });
        var http = new FakeHttp().OnPath("https://wiki.acme.com/", Fixture.Load("custom_page.json"));
        (await provider.FetchAsync(new Uri("https://wiki.acme.com/pages/42"), http, default)).StateHash.ShouldBeNull();
    }

    [Fact]
    public void Url_glob_matches_and_captures()
    {
        var provider = new CustomAppProvider(Def());
        provider.CanHandle(new Uri("https://wiki.acme.com/pages/42"), null).ShouldBeTrue();
        provider.CanHandle(new Uri("https://wiki.acme.com/other/42"), null).ShouldBeFalse();
    }

    [Fact]
    public void Definition_export_roundtrips_and_has_no_secret_fields()
    {
        var json = Def(auth: CustomAuthKind.ApiKeyHeader).ToJson();

        CustomAppDefinition.FromJson(json).ShouldBe(Def(auth: CustomAuthKind.ApiKeyHeader));
        json.ShouldContain("ApiKeyHeader");
        json.ToLowerInvariant().ShouldNotContain("token\":");
        json.ToLowerInvariant().ShouldNotContain("secret");
    }

    [Theory]
    [InlineData(CustomAuthKind.Bearer, "Authorization", "Bearer s3")]
    [InlineData(CustomAuthKind.ApiKeyHeader, "api_key", "s3")]
    public void Auth_kinds_map_to_headers(CustomAuthKind kind, string header, string value) =>
        new CustomAppProvider(Def(auth: kind)).AuthHeaders(new Credential("s3"), AuthMethod.PersonalToken).Single().ShouldBe(new(header, value));

    [Fact]
    public void Query_api_key_is_sent_as_a_query_parameter_not_a_header()
    {
        var provider = new CustomAppProvider(Def(auth: CustomAuthKind.ApiKeyQuery));
        provider.AuthHeaders(new Credential("s3"), AuthMethod.ApiKey).ShouldBeEmpty();
        provider.AuthQuery(new Credential("s3"), AuthMethod.ApiKey).Single().ShouldBe(new("api_key", "s3"));
    }
}
