using System.Text.Json;
using Noto.Core.Commands;
using Noto.Core.Links;
using Noto.Core.Models;
using Noto.Core.Recurrence;
using Noto.Providers.Preview;
using Noto.Providers.Providers;

namespace Noto.Providers.Tests;

public class LiveLinkTests
{
    const string PrUrl = "https://github.com/noto/noto-app/pull/482";
    const string JiraUrl = "https://acme.atlassian.net/browse/PROJ-2";

    static Harness Build(ReactorOptions? options = null, Guid? device = null)
    {
        var h = new Harness([new GitHubProvider(), new JiraProvider()], new PreviewOptions { MinInterval = TimeSpan.Zero });
        return h;
    }

    static LinkReactor Reactor(Harness h, ReactorOptions? o = null) => new(h.Uow, h.Bus, h.Clock, Guid.CreateVersion7(), o);

    static LinkChange Change(string url, LinkState? from, LinkState? to, string kind = "pr", string toHash = "h2") => new(
        url, from, to, "h1", toHash, new LinkPreview
        {
            Url = url, ProviderId = "github", Title = "T", Subtitle = "PROJ-2", State = to, StateHash = toHash,
            Metadata = new() { ["kind"] = JsonDocument.Parse($"\"{kind}\"").RootElement.Clone() },
        });

    static async Task<TodoItem> Load(Harness h, Guid id) => (await h.Uow.RunAsync(s => s.Items.GetAsync(id)))!;
    static Task<IReadOnlyList<ItemEvent>> Events(Harness h, Guid id) => h.Uow.RunAsync(s => s.Events.ListForItemAsync(id));

    [Fact]
    public async Task Merged_pr_suggests_done_but_does_not_complete_by_default()
    {
        using var h = Build();
        var id = await h.ItemWithLinkAsync(PrUrl);

        var outcome = await Reactor(h).ReactAsync([Change(PrUrl, LinkState.InReview, LinkState.Done)]);

        outcome.Suggestions.Single().ShouldSatisfyAllConditions(
            s => s.Kind.ShouldBe(SuggestionKind.MarkDone), s => s.Message.ShouldBe("Linked PR merged. Done?"));
        outcome.Applied.ShouldBeEmpty();
        (await Load(h, id)).Status.ShouldBe(ItemStatus.Open);
    }

    [Fact]
    public async Task Opted_in_workspace_completes_the_item_and_it_stays_undoable()
    {
        using var h = Build();
        var id = await h.ItemWithLinkAsync(PrUrl);

        var outcome = await Reactor(h, new ReactorOptions { AutoCompleteWorkspaces = [h.Workspace.Id] })
            .ReactAsync([Change(PrUrl, LinkState.InReview, LinkState.Done)]);

        outcome.Applied.Single().Kind.ShouldBe(SuggestionKind.MarkDone);
        (await Load(h, id)).Status.ShouldBe(ItemStatus.Done);
        (await Events(h, id)).Select(e => e.Type).ShouldContain(ItemEventType.Completed);
    }

    [Fact]
    public async Task Waiting_item_returns_to_today_when_its_ticket_leaves_blocked()
    {
        using var h = Build();
        var id = await h.ItemWithLinkAsync(JiraUrl);
        await h.Bus.SendAsync(new StartWaiting(id, "legal"));
        h.Clock.Advance(TimeSpan.FromDays(2)); // now Oct 9

        var outcome = await Reactor(h).ReactAsync([Change(JiraUrl, LinkState.Blocked, LinkState.InProgress, kind: "issue")]);

        outcome.Applied.Single().ShouldSatisfyAllConditions(
            s => s.Kind.ShouldBe(SuggestionKind.ReturnFromWaiting), s => s.Message.ShouldBe("PROJ-2 unblocked. Back on today."));
        var item = await Load(h, id);
        item.Status.ShouldBe(ItemStatus.Open);
        item.WaitingOn.ShouldBeNull();
        item.PlannedFor.ShouldBe(new DateOnly(2026, 10, 9));

        var events = await Events(h, id);
        events.Single(e => e.Type == ItemEventType.WaitingEnded).Data!["via"]!.GetValue<string>().ShouldBe("link");
        events.Single(e => e.Type == ItemEventType.LinkStateChanged).Data!["to_state"]!.GetValue<string>().ShouldBe("InProgress");
    }

    [Fact]
    public async Task Waiting_item_stays_put_when_the_link_changes_between_non_blocking_states()
    {
        using var h = Build();
        var id = await h.ItemWithLinkAsync(JiraUrl);
        await h.Bus.SendAsync(new StartWaiting(id, "legal"));

        var outcome = await Reactor(h).ReactAsync([Change(JiraUrl, LinkState.Open, LinkState.InProgress, kind: "issue")]);

        outcome.Applied.ShouldBeEmpty();
        (await Load(h, id)).Status.ShouldBe(ItemStatus.Waiting);
    }

    [Fact]
    public async Task Link_state_events_are_idempotent_and_deterministic_across_devices()
    {
        using var a = Build();
        using var b = Build();
        var idA = await a.ItemWithLinkAsync(PrUrl);
        var change = Change(PrUrl, LinkState.InReview, LinkState.Done);

        await Reactor(a).ReactAsync([change]);
        await Reactor(a).ReactAsync([change]); // same device observing twice

        var events = (await Events(a, idA)).Where(e => e.Type == ItemEventType.LinkStateChanged).ToList();
        events.Count.ShouldBe(1);
        events[0].Id.ShouldBe(Uuid5.Create(idA, $"{PrUrl}|h2"));

        // A second device holding the same item derives the same event id, so sync collapses them.
        await b.Bus.SendAsync(new CreateItem(idA, b.Workspace.Id, "Task", new DateOnly(2026, 10, 7)));
        await b.Uow.RunAsync(async st => { await st.Links.AddExplicitAsync(idA, PrUrl, b.Clock.UtcNow); return 0; });
        await Reactor(b).ReactAsync([change]);

        (await Events(b, idA)).Single(e => e.Type == ItemEventType.LinkStateChanged).Id.ShouldBe(events[0].Id);
    }

    [Fact]
    public async Task Unlinked_and_finished_items_are_ignored()
    {
        using var h = Build();
        var linked = await h.ItemWithLinkAsync(PrUrl);
        await h.Bus.SendAsync(new CompleteItem(linked));
        var other = await h.ItemWithLinkAsync("https://github.com/other/repo/pull/1");

        var outcome = await Reactor(h).ReactAsync([Change(PrUrl, LinkState.InReview, LinkState.Done)]);

        outcome.Suggestions.ShouldBeEmpty();
        (await Events(h, other)).Select(e => e.Type).ShouldNotContain(ItemEventType.LinkStateChanged);
    }

    // End to end: provider fixtures → PreviewService → state change → reactor.
    [Fact]
    public async Task Merged_pr_flows_from_provider_fixture_to_a_done_suggestion()
    {
        using var h = Build();
        await h.ConnectAsync("github");
        var id = await h.ItemWithLinkAsync(PrUrl);
        var open = new FakeHttp().OnPath("graphql", Fixture.Load("github_pr_open.json"));
        h.Factory.ByProvider["github"] = open;
        await h.Previews.RefreshAsync([new(PrUrl)]);

        h.Factory.ByProvider["github"] = new FakeHttp().OnPath("graphql", Fixture.Load("github_pr_merged.json"));
        var refreshed = await h.Previews.RefreshAsync([new(PrUrl, Force: true)]);
        var outcome = await Reactor(h).ReactAsync(refreshed.Changes);

        outcome.Suggestions.Single().Message.ShouldBe("Linked PR merged. Done?");
        outcome.Suggestions.Single().ItemId.ShouldBe(id);
    }

    [Fact]
    public async Task Jira_unblocking_flows_from_provider_fixture_to_an_automatic_return()
    {
        using var h = Build();
        await h.ConnectAsync("jira", "https://acme.atlassian.net");
        var id = await h.ItemWithLinkAsync(JiraUrl);
        await h.Bus.SendAsync(new StartWaiting(id, "PROJ-2"));

        h.Factory.ByProvider["jira"] = new FakeHttp().OnPath("rest/api/3/search/jql", Fixture.Load("jira_search_blocked.json"));
        await h.Previews.RefreshAsync([new(JiraUrl)]);
        (await Load(h, id)).Status.ShouldBe(ItemStatus.Waiting);

        h.Factory.ByProvider["jira"] = new FakeHttp().OnPath("rest/api/3/search/jql", Fixture.Load("jira_search_unblocked.json"));
        var refreshed = await h.Previews.RefreshAsync([new(JiraUrl, Force: true)]);
        await Reactor(h).ReactAsync(refreshed.Changes);

        var item = await Load(h, id);
        item.Status.ShouldBe(ItemStatus.Open);
        item.PlannedFor.ShouldBe(new DateOnly(2026, 10, 7));
    }

    [Fact]
    public void Rules_ignore_changes_that_are_not_finishing_or_unblocking()
    {
        var item = Make();
        LiveLinkRules.Evaluate(item, Change(PrUrl, LinkState.Open, LinkState.InReview)).ShouldBeEmpty();
        LiveLinkRules.Evaluate(item, Change(PrUrl, LinkState.Done, LinkState.Closed)).ShouldBeEmpty(); // already finished
    }

    [Fact]
    public void Closed_non_pr_links_suggest_with_the_object_name()
    {
        var s = LiveLinkRules.Evaluate(Make(), Change(JiraUrl, LinkState.InProgress, LinkState.Done, kind: "issue")).Single();
        s.Message.ShouldBe("PROJ-2 closed. Done?");
    }

    static TodoItem Make() => Core.Tests.Make.Item();
}

public class TitleSuggesterTests
{
    static LinkPreview P(string provider, string kind, string? number, string title = "Add previews") => new()
    {
        ProviderId = provider, Title = title,
        Metadata = new()
        {
            ["kind"] = JsonDocument.Parse($"\"{kind}\"").RootElement.Clone(),
            ["number"] = JsonDocument.Parse(number is null ? "null" : $"\"{number}\"").RootElement.Clone(),
        },
    };

    [Fact]
    public void Pr_becomes_a_review_todo() => TitleSuggester.ForPaste(P("github", "pr", "482")).ShouldBe("Review: Add previews (#482)");

    [Fact]
    public void Tickets_lead_with_their_key() => TitleSuggester.ForPaste(P("jira", "issue", "PROJ-9")).ShouldBe("PROJ-9: Add previews");

    [Fact]
    public void Plain_pages_use_their_title() => TitleSuggester.ForPaste(P("notion", "page", null)).ShouldBe("Add previews");
}

public class RefreshPlannerTests
{
    static readonly DateOnly Today = new(2026, 10, 7);

    static TodoItem Item(DateOnly? planned, ItemStatus status = ItemStatus.Open)
    {
        var i = Core.Tests.Make.Item();
        i.PlannedFor = planned;
        i.Status = status;
        if (status == ItemStatus.Waiting) i.WaitingOn = "x";
        return i;
    }

    static TodoLink L(TodoItem i, string url) => new(Guid.CreateVersion7(), i.Id, url, 0, DateTimeOffset.UnixEpoch, "text");

    [Fact]
    public void Visible_then_waiting_or_today_then_rest()
    {
        var visible = Item(Today.AddDays(5));
        var waiting = Item(null, ItemStatus.Waiting);
        var today = Item(Today);
        var later = Item(Today.AddDays(9));
        var items = new[] { visible, waiting, today, later };
        var links = items.Select((i, n) => L(i, $"https://x.test/{n}")).ToList();

        var plan = RefreshPlanner.Plan(items, links, Today, new HashSet<Guid> { visible.Id });

        plan.Select(r => r.Priority).ShouldBe([PreviewPriority.Visible, PreviewPriority.WaitingOrToday, PreviewPriority.WaitingOrToday, PreviewPriority.Rest]);
        plan[0].Url.ShouldBe("https://x.test/0");
    }

    [Fact]
    public void Shared_urls_take_the_best_priority_and_finished_items_are_skipped()
    {
        var today = Item(Today);
        var later = Item(Today.AddDays(9));
        var done = Item(Today, ItemStatus.Done);
        var links = new[] { L(later, "https://x.test/a"), L(today, "https://x.test/a"), L(done, "https://x.test/b") };

        var plan = RefreshPlanner.Plan([today, later, done], links, Today, new HashSet<Guid>());

        plan.ShouldHaveSingleItem().Priority.ShouldBe(PreviewPriority.WaitingOrToday);
    }

    [Fact]
    public void Foreground_cycle_covers_only_waiting_or_today_and_runs_every_15_minutes()
    {
        var today = Item(Today);
        var later = Item(Today.AddDays(9));
        var cycle = RefreshPlanner.ForegroundCycleRequests([today, later], [L(today, "https://x.test/a"), L(later, "https://x.test/b")], Today);

        cycle.Select(r => r.Url).ShouldBe(["https://x.test/a"]);
        var now = DateTimeOffset.Parse("2026-10-07T10:00:00Z");
        RefreshPlanner.CycleDue(null, now).ShouldBeTrue();
        RefreshPlanner.CycleDue(now.AddMinutes(-14), now).ShouldBeFalse();
        RefreshPlanner.CycleDue(now.AddMinutes(-15), now).ShouldBeTrue();
    }
}
