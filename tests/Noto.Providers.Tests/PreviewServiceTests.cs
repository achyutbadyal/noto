using Noto.Core.Links;
using Noto.Providers.Preview;

namespace Noto.Providers.Tests;

public class PreviewServiceTests
{
    static readonly PreviewOptions Opts = new() { MinInterval = TimeSpan.FromSeconds(1), BackoffBase = TimeSpan.FromSeconds(5) };

    static (Harness H, ScriptedProvider P) Build(string id = "fake")
    {
        var p = new ScriptedProvider(id);
        var h = new Harness([p], Opts);
        h.Factory.ByProvider[id] = new FakeHttp();
        return (h, p);
    }

    static string Url(string path = "a", string host = "fake.test") => LinkUrl.Normalize($"https://{host}/{path}")!;

    [Fact]
    public async Task First_fetch_stores_a_chip_and_later_calls_are_served_from_cache()
    {
        var (h, p) = Build();
        await h.ConnectAsync("fake");

        var first = await h.Previews.RefreshAsync([new(Url())]);
        first.Previews[Url()].ChipFacts.ShouldNotBeEmpty();
        first.Previews[Url()].ExpiresAt.ShouldBe(h.Clock.UtcNow + TimeSpan.FromMinutes(5));

        await h.Previews.RefreshAsync([new(Url())]);

        p.Calls.Count.ShouldBe(1);
        (await h.Previews.GetCachedAsync([Url()]))[Url()].Status.ShouldBe(PreviewStatus.Loaded); // no network needed
    }

    [Fact]
    public async Task Expired_entries_and_forced_requests_refetch()
    {
        var (h, p) = Build();
        await h.ConnectAsync("fake");
        await h.Previews.RefreshAsync([new(Url())]);

        h.Clock.Advance(TimeSpan.FromMinutes(6));
        await h.Previews.RefreshAsync([new(Url())]);
        await h.Previews.RefreshAsync([new(Url(), Force: true)]);

        p.Calls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Urls_without_a_connection_are_plain_links()
    {
        var (h, p) = Build();

        var result = await h.Previews.RefreshAsync([new(Url())]);

        p.Calls.ShouldBeEmpty();
        result.Previews.ShouldBeEmpty();
    }

    [Fact]
    public async Task Same_provider_urls_share_one_batched_call_visible_first()
    {
        var (h, p) = Build();
        await h.ConnectAsync("fake");

        await h.Previews.RefreshAsync([new(Url("rest"), PreviewPriority.Rest), new(Url("visible"), PreviewPriority.Visible), new(Url("today"), PreviewPriority.WaitingOrToday)]);

        p.Calls.Count.ShouldBe(1);
        p.Calls[0].Select(u => u.AbsolutePath).ShouldBe(["/visible", "/today", "/rest"]);
    }

    [Fact]
    public async Task Providers_with_visible_work_are_served_before_the_rest()
    {
        var alpha = new ScriptedProvider("alpha");
        var beta = new ScriptedProvider("beta");
        var order = new List<string>();
        alpha.Script = u => { order.Add("alpha"); return u.Select(x => alpha.Preview(x, LinkState.Open, "h")).ToList(); };
        beta.Script = u => { order.Add("beta"); return u.Select(x => beta.Preview(x, LinkState.Open, "h")).ToList(); };
        var h = new Harness([alpha, beta], Opts);
        h.Factory.ByProvider["alpha"] = new FakeHttp();
        h.Factory.ByProvider["beta"] = new FakeHttp();
        await h.ConnectAsync("alpha");
        await h.ConnectAsync("beta");

        await h.Previews.RefreshAsync([new(Url("x", "alpha.test"), PreviewPriority.Rest), new(Url("y", "beta.test"), PreviewPriority.Visible)]);

        order.ShouldBe(["beta", "alpha"]);
    }

    [Fact]
    public async Task Calls_to_one_provider_connection_are_spaced_by_the_min_interval()
    {
        var (h, _) = Build();
        await h.ConnectAsync("fake");

        await h.Previews.RefreshAsync([new(Url("a"))]);
        h.Delay.Delays.ShouldBeEmpty();
        await h.Previews.RefreshAsync([new(Url("b"))]);

        h.Delay.Delays.ShouldBe([TimeSpan.FromSeconds(1)]);
    }

    [Fact]
    public async Task Rate_limit_backs_off_honouring_retry_after_and_serves_cache_meanwhile()
    {
        var (h, p) = Build();
        await h.ConnectAsync("fake");
        await h.Previews.RefreshAsync([new(Url())]);

        p.Throw = new Noto.Providers.ProviderHttpException(429, TimeSpan.FromSeconds(60));
        var limited = await h.Previews.RefreshAsync([new(Url(), Force: true)]);
        limited.Previews[Url()].Status.ShouldBe(PreviewStatus.Stale);
        limited.Previews[Url()].ChipFacts.ShouldNotBeEmpty(); // cached facts survive

        p.Throw = null;
        var callsBefore = p.Calls.Count;
        h.Clock.Advance(TimeSpan.FromSeconds(30));
        await h.Previews.RefreshAsync([new(Url(), Force: true)]);
        p.Calls.Count.ShouldBe(callsBefore); // still backing off, no request sent

        h.Clock.Advance(TimeSpan.FromSeconds(31));
        await h.Previews.RefreshAsync([new(Url(), Force: true)]);
        p.Calls.Count.ShouldBe(callsBefore + 1);
    }

    [Fact]
    public async Task Backoff_grows_exponentially_without_retry_after()
    {
        var (h, p) = Build();
        await h.ConnectAsync("fake");
        p.Throw = new Noto.Providers.ProviderHttpException(429);

        await h.Previews.RefreshAsync([new(Url())]);          // fails → 5s
        h.Clock.Advance(TimeSpan.FromSeconds(6));
        await h.Previews.RefreshAsync([new(Url())]);          // fails → 10s
        var calls = p.Calls.Count;
        h.Clock.Advance(TimeSpan.FromSeconds(7));
        await h.Previews.RefreshAsync([new(Url())]);

        p.Calls.Count.ShouldBe(calls); // 7s < 10s: still blocked
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Auth_failures_prompt_reconnect_and_mark_the_connection(int status)
    {
        var (h, p) = Build();
        var c = await h.ConnectAsync("fake");
        p.Throw = new Noto.Providers.ProviderHttpException(status);

        var result = await h.Previews.RefreshAsync([new(Url())]);

        result.Previews[Url()].Status.ShouldBe(PreviewStatus.AuthRequired);
        (await h.Uow.RunAsync(s => s.Connections.GetAsync(c.Id)))!.Status.ShouldBe(ConnectionStatus.Expired);
    }

    [Fact]
    public async Task Missing_credential_is_auth_required()
    {
        var (h, _) = Build();
        var c = await h.ConnectAsync("fake");
        await h.Credentials.DeleteAsync(c.Id);
        // Real transports ask the credential source; the fake factory doesn't, so simulate that call.
        h.Factory.ByProvider["fake"] = new FakeHttp();
        ((ScriptedProvider)h.Registry.Get("fake")!).Throw = new Noto.Providers.Auth.AuthRequiredException("No credential stored");

        (await h.Previews.RefreshAsync([new(Url())])).Previews[Url()].Status.ShouldBe(PreviewStatus.AuthRequired);
    }

    [Fact]
    public async Task Gone_objects_are_unavailable()
    {
        var (h, p) = Build();
        await h.ConnectAsync("fake");
        p.Throw = new Noto.Providers.ProviderHttpException(404);

        var r = await h.Previews.RefreshAsync([new(Url())]);

        r.Previews[Url()].Status.ShouldBe(PreviewStatus.Unavailable);
        r.Previews[Url()].ChipFacts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Network_errors_keep_cached_facts_and_flag_an_error()
    {
        var (h, p) = Build();
        await h.ConnectAsync("fake");
        await h.Previews.RefreshAsync([new(Url())]);

        p.Throw = new HttpRequestException("offline");
        var r = await h.Previews.RefreshAsync([new(Url(), Force: true)]);

        r.Previews[Url()].Status.ShouldBe(PreviewStatus.Error);
        r.Previews[Url()].ChipFacts.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Change_dot_appears_when_the_state_hash_moves_and_clears_when_viewed()
    {
        var (h, p) = Build();
        await h.ConnectAsync("fake");
        var hash = "h1";
        p.Script = u => u.Select(x => p.Preview(x, LinkState.Open, hash)).ToList();

        var first = await h.Previews.RefreshAsync([new(Url())]);
        first.Previews[Url()].HasChange.ShouldBeFalse(); // a new link starts "seen"
        first.Changes.ShouldBeEmpty();

        hash = "h2";
        var second = await h.Previews.RefreshAsync([new(Url(), Force: true)]);
        second.Previews[Url()].HasChange.ShouldBeTrue();
        second.Changes.Single().ShouldSatisfyAllConditions(c => c.FromHash.ShouldBe("h1"), c => c.ToHash.ShouldBe("h2"));

        (await h.Previews.GetCachedAsync([Url()]))[Url()].HasChange.ShouldBeTrue();
        await h.Previews.MarkViewedAsync(Url());
        (await h.Previews.GetCachedAsync([Url()]))[Url()].HasChange.ShouldBeFalse();
    }

    [Fact]
    public async Task Unchanged_state_produces_no_change_event()
    {
        var (h, _) = Build();
        await h.ConnectAsync("fake");
        await h.Previews.RefreshAsync([new(Url())]);
        (await h.Previews.RefreshAsync([new(Url(), Force: true)])).Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Disconnecting_clears_that_connections_previews()
    {
        var (h, _) = Build();
        var c = await h.ConnectAsync("fake");
        await h.Previews.RefreshAsync([new(Url())]);

        await h.Uow.RunAsync(async s => { await s.Previews.DeleteForConnectionAsync(c.Id); return 0; });

        (await h.Previews.GetCachedAsync([Url()])).ShouldBeEmpty();
    }
}

public class DebouncerTests
{
    sealed class ManualDelay : IDelay
    {
        public List<TaskCompletionSource> Pending { get; } = [];

        public Task DelayAsync(TimeSpan time, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            lock (Pending) Pending.Add(tcs);
            return tcs.Task;
        }
    }

    [Fact]
    public async Task Only_the_last_edit_in_a_burst_runs()
    {
        var delay = new ManualDelay();
        var debounce = new Debouncer(delay, TimeSpan.FromMilliseconds(500));
        var ran = new List<string>();

        var a = debounce.RunAsync("item", () => { ran.Add("a"); return Task.CompletedTask; });
        var b = debounce.RunAsync("item", () => { ran.Add("b"); return Task.CompletedTask; });
        await a; // superseded
        delay.Pending[1].SetResult();
        await b;

        ran.ShouldBe(["b"]);
    }

    [Fact]
    public async Task Different_keys_do_not_cancel_each_other()
    {
        var delay = new ManualDelay();
        var debounce = new Debouncer(delay, TimeSpan.FromMilliseconds(500));
        var ran = new List<string>();

        var a = debounce.RunAsync("one", () => { ran.Add("one"); return Task.CompletedTask; });
        var b = debounce.RunAsync("two", () => { ran.Add("two"); return Task.CompletedTask; });
        delay.Pending.ForEach(t => t.SetResult());
        await Task.WhenAll(a, b);

        ran.Order().ShouldBe(["one", "two"]);
    }
}
