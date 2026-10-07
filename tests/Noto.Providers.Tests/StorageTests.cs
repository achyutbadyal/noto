using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Noto.Core.Commands;
using Noto.Core.Links;
using Noto.Core.Tests;
using Noto.Data;
using Noto.Providers.Auth;
using Noto.Providers.Providers;
using Noto.Providers.Transport;

namespace Noto.Providers.Tests;

public class LinkStorageTests : IDisposable
{
    readonly Harness _h = new([new GitHubProvider()]);
    readonly LinkIndexer _indexer;

    public LinkStorageTests() => _indexer = new LinkIndexer(_h.Uow, _h.Clock);

    public void Dispose() => _h.Dispose();

    Task<IReadOnlyList<TodoLink>> Links(Guid item) =>
        _h.Uow.RunAsync(s => s.Links.ListForItemAsync(item));

    [Fact]
    public async Task Indexing_title_and_notes_creates_ordered_normalized_links()
    {
        var id = await _h.ItemWithLinkAsync("https://example.com/seed", "seed");
        var links = await _indexer.IndexAsync(
            id,
            "Review https://GitHub.com/a/b/pull/1?utm_source=x",
            "see https://example.com/doc"
        );

        links
            .Where(l => l.Source == "text")
            .Select(l => (l.Url, l.Position))
            .ShouldBe([("https://github.com/a/b/pull/1", 0), ("https://example.com/doc", 1)]);
    }

    [Fact]
    public async Task Links_removed_from_text_are_tombstoned_but_explicit_links_stay()
    {
        var id = await _h.ItemWithLinkAsync("https://example.com/explicit");
        await _indexer.IndexAsync(id, "https://example.com/a", null);
        await _indexer.IndexAsync(id, "no links now", null);

        (await Links(id)).Select(l => l.Url).ShouldBe(["https://example.com/explicit"]);
    }

    [Fact]
    public async Task Re_adding_a_removed_link_revives_the_same_row()
    {
        var id = await _h.ItemWithLinkAsync("https://example.com/x");
        var first = (await _indexer.IndexAsync(id, "https://example.com/a", null)).Single(l =>
            l.Source == "text"
        );
        await _indexer.IndexAsync(id, "", null);
        var again = (await _indexer.IndexAsync(id, "https://example.com/a", null)).Single(l =>
            l.Source == "text"
        );

        again.Id.ShouldBe(first.Id); // UUIDv5(item, url): stable across devices
    }

    [Fact]
    public async Task Link_ids_are_deterministic_so_two_devices_merge()
    {
        var id = await _h.ItemWithLinkAsync("https://example.com/x");
        var link = (await Links(id)).Single();
        link.Id.ShouldBe(Noto.Core.Recurrence.Uuid5.Create(id, "https://example.com/x"));
    }

    [Fact]
    public async Task Items_by_url_lookup_ignores_removed_links()
    {
        var id = await _h.ItemWithLinkAsync("https://example.com/shared");
        var other = await _h.ItemWithLinkAsync("https://example.com/shared");
        await _h.Uow.RunAsync(async s =>
        {
            await s.Links.RemoveAsync(
                (await s.Links.ListForItemAsync(other)).Single().Id,
                _h.Clock.UtcNow
            );
            return 0;
        });

        (
            await _h.Uow.RunAsync(s => s.Links.ListItemIdsForUrlAsync("https://example.com/shared"))
        ).ShouldBe([id]);
    }

    [Fact]
    public async Task Non_http_explicit_links_are_rejected()
    {
        var id = await _h.ItemWithLinkAsync("https://example.com/x");
        await Should.ThrowAsync<ArgumentException>(() =>
            _indexer.AddExplicitAsync(id, "javascript:alert(1)")
        );
    }

    [Fact]
    public async Task Preview_cache_round_trips_and_keeps_viewed_columns_across_upserts()
    {
        var preview = new LinkPreview
        {
            Url = "https://github.com/a/b/pull/1",
            ProviderId = "github",
            Title = "T",
            State = LinkState.InReview,
            StateHash = "h1",
            ChipFacts = [new("2 approved", "g")],
            Status = PreviewStatus.Loaded,
            FetchedAt = _h.Clock.UtcNow,
            ViewedStateHash = "h1",
            Metadata = new() { ["kind"] = JsonDocument.Parse("\"pr\"").RootElement.Clone() },
        };
        await _h.Uow.RunAsync(async s =>
        {
            await s.Previews.PutAsync(preview);
            return 0;
        });

        preview.StateHash = "h2";
        preview.ViewedStateHash = null; // upsert must not clobber what the user viewed
        await _h.Uow.RunAsync(async s =>
        {
            await s.Previews.PutAsync(preview);
            return 0;
        });

        var read = (await _h.Uow.RunAsync(s => s.Previews.GetAsync(preview.Url)))!;
        (read.StateHash, read.ViewedStateHash, read.HasChange).ShouldBe(("h2", "h1", true));
        read.ChipFacts.Single().Text.ShouldBe("2 approved");
        read.Metadata["kind"].GetString().ShouldBe("pr");
        read.State.ShouldBe(LinkState.InReview);
    }

    [Fact]
    public async Task Connection_repository_round_trips_without_any_secret_column()
    {
        var c = await _h.ConnectAsync("github", "https://ghe.acme.com", token: "ghp_secret");
        var read = (await _h.Uow.RunAsync(s => s.Connections.GetAsync(c.Id)))!;
        (read.ProviderId, read.InstanceUrl, read.Status, read.AuthMethod).ShouldBe(
            ("github", "https://ghe.acme.com", ConnectionStatus.Active, AuthMethod.PersonalToken)
        );

        await _h.Uow.RunAsync(async s =>
        {
            await s.Connections.DeleteAsync(c.Id);
            return 0;
        });
        (await _h.Uow.RunAsync(s => s.Connections.ListAsync())).ShouldBeEmpty();
    }
}

public class TokenIsolationTests : IDisposable
{
    const string Secret = "ghp_SUPER_SECRET_TOKEN_123";

    readonly string _db = Path.Combine(
        AppContext.BaseDirectory,
        $"isolation-{Guid.NewGuid():N}.db"
    );
    readonly Harness _h;
    readonly FakeHttp _github = new FakeHttp()
        .OnPath("user", """{ "login": "dana" }""")
        .OnPath("graphql", Fixture.Load("github_batch.json"));
    readonly CollectingLogger<DirectTransport> _transportLog = new();

    public TokenIsolationTests()
    {
        _h = new Harness([new GitHubProvider(), new OpenGraphProvider()], dbPath: _db);
        _h.Factory.ByProvider["github"] = _github;
    }

    public void Dispose()
    {
        _h.Dispose();
        foreach (var f in Directory.GetFiles(AppContext.BaseDirectory, Path.GetFileName(_db) + "*"))
            File.Delete(f);
    }

    ConnectionService Service() =>
        new(
            _h.Uow,
            _h.Credentials,
            _h.Registry,
            _h.Factory,
            _h.Clock,
            new HttpClient(),
            new Dictionary<string, OAuthClient>()
        );

    // Every table, every column, plus the raw database and WAL bytes.
    string DumpDatabase()
    {
        var sb = new System.Text.StringBuilder();
        using (var conn = new SqliteConnection($"Data Source={_db}"))
        {
            conn.Open();
            var tables = new List<string>();
            using (
                var cmd = new SqliteCommand(
                    "SELECT name FROM sqlite_master WHERE type = 'table'",
                    conn
                )
            )
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    tables.Add(r.GetString(0));
            foreach (var t in tables)
            {
                using var cmd = new SqliteCommand($"SELECT * FROM \"{t}\"", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    for (var i = 0; i < r.FieldCount; i++)
                        sb.Append(t)
                            .Append('.')
                            .Append(r.GetName(i))
                            .Append('=')
                            .AppendLine(Convert.ToString(r.GetValue(i)));
            }
        }
        foreach (var path in new[] { _db, _db + "-wal" }.Where(File.Exists))
        {
            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );
            using var reader = new StreamReader(fs, System.Text.Encoding.Latin1);
            sb.Append(reader.ReadToEnd());
        }
        return sb.ToString();
    }

    [Fact]
    public async Task A_pat_lives_only_in_the_keyring()
    {
        var c = await Service().ConnectWithTokenAsync("github", Secret, AuthMethod.PersonalToken);
        await _h.ItemWithLinkAsync("https://github.com/noto/noto-app/pull/482");
        await _h.Previews.RefreshAsync([new("https://github.com/noto/noto-app/pull/482")]);

        (await _h.Keyring.GetAsync(CredentialStore.Service, c.Id.ToString()))!.ShouldContain(
            Secret
        );
        DumpDatabase().ShouldNotContain(Secret);
        c.DisplayLabel.ShouldBe("dana");
    }

    [Fact]
    public async Task Connection_export_contains_no_secrets()
    {
        await Service()
            .ConnectWithTokenAsync(
                "github",
                Secret,
                AuthMethod.PersonalToken,
                instanceUrl: "https://ghe.acme.com"
            );
        var export = await Service().ExportMetadataAsync();

        export.ShouldNotContain(Secret);
        export.ShouldContain("ghe.acme.com");
    }

    [Fact]
    public async Task Logs_never_contain_tokens_even_when_requests_fail()
    {
        var def = new Noto.Providers.CustomApp.CustomAppDefinition(
            Guid.NewGuid(),
            "Wiki",
            "wiki.acme.com/pages/{id}",
            Noto.Providers.CustomApp.CustomAuthKind.ApiKeyQuery,
            "api_key",
            Noto.Providers.CustomApp.CustomPreviewMode.JsonApi,
            "https://wiki.acme.com/api/pages/{id}",
            "$.title"
        );
        var provider = new Noto.Providers.CustomApp.CustomAppProvider(def);
        var handler = new FakeHandler((_, _) => FakeHandler.Json("""{ "error": "boom" }""", 500));
        var transport = new DirectTransport(
            new HttpClient(handler),
            provider,
            null,
            AuthMethod.ApiKey,
            new StaticCredentialSource(new Credential(Secret)),
            _transportLog
        );

        var ex = await Should.ThrowAsync<ProviderHttpException>(() =>
            provider.FetchAsync(new Uri("https://wiki.acme.com/pages/7"), transport, default)
        );

        handler.Calls.Single().Request.RequestUri!.Query.ShouldContain(Secret); // it is sent...
        _transportLog.Lines.ShouldNotBeEmpty();
        _transportLog.Lines.ShouldAllBe(l => !l.Contains(Secret)); // ...but never logged
        ex.Message.ShouldNotContain(Secret);
    }

    [Fact]
    public async Task Preview_service_logs_omit_tokens_on_failure()
    {
        await Service().ConnectWithTokenAsync("github", Secret, AuthMethod.PersonalToken);
        await _h.ItemWithLinkAsync("https://github.com/noto/noto-app/pull/482");
        _h.Factory.ByProvider["github"] = new ThrowingHttp(
            new HttpRequestException($"connect failed Authorization: Bearer {Secret}")
        );

        var result = await _h.Previews.RefreshAsync([
            new("https://github.com/noto/noto-app/pull/482"),
        ]);

        result.Previews.Values.Single().Status.ShouldBe(PreviewStatus.Error);
        _h.Log.Lines.ShouldAllBe(l => !l.Contains(Secret));
        DumpDatabase().ShouldNotContain(Secret);
    }

    [Fact]
    public async Task Failed_validation_stores_nothing()
    {
        _h.Factory.ByProvider["github"] = new FakeHttp().OnPath("user", "{}", status: 401);

        await Should.ThrowAsync<ProviderHttpException>(() =>
            Service().ConnectWithTokenAsync("github", Secret, AuthMethod.PersonalToken)
        );

        (await _h.Uow.RunAsync(s => s.Connections.ListAsync())).ShouldBeEmpty();
        DumpDatabase().ShouldNotContain(Secret);
    }

    [Fact]
    public async Task Disconnect_removes_keyring_item_previews_and_connection()
    {
        var c = await Service().ConnectWithTokenAsync("github", Secret, AuthMethod.PersonalToken);
        await _h.ItemWithLinkAsync("https://github.com/noto/noto-app/pull/482");
        await _h.Previews.RefreshAsync([new("https://github.com/noto/noto-app/pull/482")]);

        await Service().DisconnectAsync(c.Id);

        (await _h.Keyring.GetAsync(CredentialStore.Service, c.Id.ToString())).ShouldBeNull();
        (await _h.Uow.RunAsync(s => s.Connections.ListAsync())).ShouldBeEmpty();
        (
            await _h.Previews.GetCachedAsync(["https://github.com/noto/noto-app/pull/482"])
        ).ShouldBeEmpty();
    }

    [Fact]
    public async Task Unsupported_auth_method_is_refused()
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            Service().ConnectWithTokenAsync("github", Secret, AuthMethod.BasicAuth)
        );
    }

    [Fact]
    public async Task Oauth_flow_end_to_end_with_a_fake_browser()
    {
        var provider = new ScriptedProvider("fake")
        {
            Config = new("https://auth.test/authorize", "https://auth.test/token", ["read"], ""),
        };
        var h = new Harness([provider]);
        h.Factory.ByProvider["fake"] = new FakeHttp();
        var tokenEndpoint = new FakeHandler(
            (_, _) =>
                FakeHandler.Json(
                    """{ "access_token": "oauth-at", "refresh_token": "oauth-rt", "expires_in": 3600 }"""
                )
        );
        var service = new ConnectionService(
            h.Uow,
            h.Credentials,
            h.Registry,
            h.Factory,
            h.Clock,
            new HttpClient(tokenEndpoint),
            new Dictionary<string, OAuthClient> { ["fake"] = new("cid") }
        );

        var connection = await service.ConnectOAuthAsync(
            "fake",
            async authorize =>
            {
                var q = System.Web.HttpUtility.ParseQueryString(authorize.Query);
                q["code_challenge_method"].ShouldBe("S256");
                using var http = new HttpClient();
                await http.GetAsync($"{q["redirect_uri"]}?code=abc&state={q["state"]}");
            }
        );

        connection.AuthMethod.ShouldBe(AuthMethod.OAuth2);
        var stored = (await h.Credentials.LoadAsync(connection.Id))!;
        (stored.AccessToken, stored.RefreshToken).ShouldBe(("oauth-at", "oauth-rt"));
        tokenEndpoint.Calls.Single().Body.ShouldContain("code_verifier=");
    }

    sealed class ThrowingHttp(Exception e) : IProviderHttp
    {
        public Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct) =>
            throw e;

        public Task<OpenGraphData?> OpenGraphAsync(Uri url, CancellationToken ct) => throw e;
    }
}
