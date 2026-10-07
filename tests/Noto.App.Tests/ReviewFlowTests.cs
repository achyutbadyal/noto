using Noto.App.Logic;
using Noto.App.ViewModels;
using Noto.Core.Models;

namespace Noto.App.Tests;

public sealed class ReviewFlowTests : IDisposable
{
    readonly AppFixture _app = new();

    public void Dispose() => _app.Dispose();

    // Five items planned three days ago, still open today.
    async Task<(Guid[] Ids, ReviewViewModel Review)> SeedAsync(ReviewMode mode = ReviewMode.Morning)
    {
        var ids = new List<Guid>();
        var past = AppFixture.Today.AddDays(-3);
        for (var n = 1; n <= 5; n++)
            ids.Add(await _app.AddAsync($"Task {n}", past));
        var review = new ReviewViewModel(_app.Services, _app.Workspace.Id, mode);
        await review.LoadAsync();
        return (ids.ToArray(), review);
    }

    static KeyChord Key(string key) => new(key);

    [Fact]
    public async Task Loads_every_carried_item_with_a_greeting()
    {
        var (_, review) = await SeedAsync();

        review.Entries.Count.ShouldBe(5);
        review.Greeting.ShouldBe("Good morning. 5 items carried over from yesterday.");
        review.ProgressText.ShouldBe("1 / 5");
    }

    // Acceptance (docs/08 Phase 3): a 5-item review is doable with the keyboard alone, in a handful of keystrokes.
    [Fact]
    public async Task Five_items_can_be_reviewed_with_the_keyboard_alone()
    {
        var (ids, review) = await SeedAsync();
        var keys = 0;
        async Task Press(string key)
        {
            keys++;
            await review.HandleKeyAsync(Key(key));
        }

        await Press("t"); // Task 1: keep for today
        await Press("d");
        review.Decisions.Prompt!.Text = "fri";
        await Press("Enter"); // Task 2: defer to Friday
        await Press("s"); // Task 3: someday
        await Press("x");
        await Press("Enter"); // Task 4: drop (default reason)
        await Press("a"); // Task 5: already done

        review.IsComplete.ShouldBeTrue();
        review.Entries.ShouldAllBe(e => e.IsDecided);
        keys.ShouldBeLessThanOrEqualTo(8); // well inside 30 seconds for a human

        (await _app.GetAsync(ids[0])).PlannedFor.ShouldBe(AppFixture.Today);
        (await _app.GetAsync(ids[1])).PlannedFor.ShouldBe(new DateOnly(2026, 10, 9));
        (await _app.GetAsync(ids[2])).IsSomeday.ShouldBeTrue();
        (await _app.GetAsync(ids[3])).Status.ShouldBe(ItemStatus.Dropped);
        var done = await _app.GetAsync(ids[4]);
        done.Status.ShouldBe(ItemStatus.Done);
        done.CompletedOn.ShouldBe(AppFixture.Today.AddDays(-1)); // credited to yesterday
    }

    [Fact]
    public async Task Every_decision_is_undoable_and_returns_the_item_to_review()
    {
        var (ids, review) = await SeedAsync();
        var original = await Task.WhenAll(ids.Select(_app.GetAsync));

        await review.HandleKeyAsync(Key("t"));
        await review.HandleKeyAsync(Key("s"));
        await review.HandleKeyAsync(Key("w"));
        review.Decisions.Prompt!.Text = "Priya";
        await review.HandleKeyAsync(Key("Enter"));
        await review.HandleKeyAsync(Key("b"));
        review.Decisions.Prompt!.Text = "a; b";
        await review.HandleKeyAsync(Key("Enter"));
        await review.HandleKeyAsync(Key("x"));
        await review.HandleKeyAsync(Key("2"));

        // Undo newest-first, as ⌘Z does.
        for (var i = 0; i < 5; i++)
            (await review.UndoAsync()).ShouldBeTrue();

        review.Entries.ShouldAllBe(e => !e.IsDecided);
        review.Index.ShouldBe(0);
        for (var n = 0; n < 5; n++)
        {
            var now = await _app.GetAsync(ids[n]);
            (now.Status, now.IsSomeday, now.PlannedFor, now.IsContainer, now.WaitingOn).ShouldBe(
                (
                    original[n].Status,
                    original[n].IsSomeday,
                    original[n].PlannedFor,
                    original[n].IsContainer,
                    original[n].WaitingOn
                )
            );
        }
    }

    [Fact]
    public async Task Deferring_to_a_past_date_is_rejected_and_keeps_the_prompt_open()
    {
        var (ids, review) = await SeedAsync();
        await review.HandleKeyAsync(Key("d"));
        review.Decisions.Prompt!.Text = "+0";
        await review.HandleKeyAsync(Key("Enter"));

        review.Decisions.Prompt.ShouldNotBeNull();
        review.Decisions.Prompt!.Error.ShouldNotBeNull();
        review.Entries[0].IsDecided.ShouldBeFalse();
        (await _app.GetAsync(ids[0])).PlannedFor.ShouldBe(AppFixture.Today.AddDays(-3));
    }

    [Fact]
    public async Task Escape_cancels_a_prompt_without_deciding()
    {
        var (_, review) = await SeedAsync();
        await review.HandleKeyAsync(Key("w"));
        await review.HandleKeyAsync(Key("Escape"));

        review.Decisions.Prompt.ShouldBeNull();
        review.Entries[0].IsDecided.ShouldBeFalse();
    }

    [Fact]
    public async Task Arrow_keys_move_between_items_and_decided_items_are_protected()
    {
        var (_, review) = await SeedAsync();
        await review.HandleKeyAsync(Key("ArrowRight"));
        review.Index.ShouldBe(1);
        await review.HandleKeyAsync(Key("ArrowLeft"));
        review.Index.ShouldBe(0);

        await review.HandleKeyAsync(Key("t"));
        await review.HandleKeyAsync(Key("ArrowLeft")); // back to the decided item
        review.Index.ShouldBe(0);
        await review.HandleKeyAsync(Key("s"));
        review.Message.ShouldNotBeNull();
        review.Entries[0].Decision.ShouldNotBeNull();
    }

    [Fact]
    public async Task Capacity_meter_updates_live_as_items_are_kept()
    {
        var past = AppFixture.Today.AddDays(-2);
        await _app.AddAsync("A", past, estimate: 120);
        await _app.AddAsync("B", past, estimate: 120);
        var review = new ReviewViewModel(_app.Services, _app.Workspace.Id, ReviewMode.Morning);
        await review.LoadAsync();

        review.Capacity!.Summary.Committed.ShouldBe(0);
        review.KeepAllText.ShouldBe("Fits: 4h of 6h");

        await review.HandleKeyAsync(Key("t"));
        review.Capacity!.Summary.Committed.ShouldBe(120);
    }

    [Fact]
    public async Task Keep_all_commits_everything_and_each_is_undoable()
    {
        var (ids, review) = await SeedAsync();
        await review.KeepAllAsync();

        review.IsComplete.ShouldBeTrue();
        foreach (var id in ids)
            (await _app.GetAsync(id)).PlannedFor.ShouldBe(AppFixture.Today);
        await review.UndoAsync();
        (await _app.GetAsync(ids[^1])).PlannedFor.ShouldBe(AppFixture.Today.AddDays(-3));
    }

    [Fact]
    public async Task Shutdown_mode_moves_leftovers_to_tomorrow_as_a_defer()
    {
        var id = await _app.AddAsync("Unfinished", AppFixture.Today);
        var review = new ReviewViewModel(_app.Services, _app.Workspace.Id, ReviewMode.Shutdown);
        await review.LoadAsync();

        review.Entries.Count.ShouldBe(1);
        await review.HandleKeyAsync(Key("t"));

        (await _app.GetAsync(id)).PlannedFor.ShouldBe(AppFixture.Today.AddDays(1));
        var metrics = (await _app.Services.Reader.LoadAsync(_app.Workspace.Id)).Metrics;
        metrics.Count.ShouldBe(1); // still a live item
        (await _app.Services.Derive.GetMetricsAsync(_app.Workspace.Id, [await _app.GetAsync(id)]))[
            id
        ]
            .Defers.ShouldBe(1);
    }

    [Fact]
    public async Task Skipping_remembers_the_dismissal_for_today()
    {
        var (_, review) = await SeedAsync();
        var closed = false;
        review.Closed += () => closed = true;

        await review.HandleKeyAsync(Key("Escape"));

        closed.ShouldBeTrue();
        _app.Services.UiState.Get(ReviewViewModel.DismissKey(_app.Workspace.Id))
            .ShouldBe("2026-10-07");
    }

    [Fact]
    public async Task Suggests_breaking_down_big_repeatedly_carried_items()
    {
        // Created four days ago for that day, so four boundaries have been crossed.
        _app.Clock.Advance(TimeSpan.FromDays(-4));
        await _app.AddAsync("Migrate auth", AppFixture.Today.AddDays(-4), estimate: 180);
        _app.Clock.Advance(TimeSpan.FromDays(4));
        var review = new ReviewViewModel(_app.Services, _app.Workspace.Id, ReviewMode.Morning);
        await review.LoadAsync();

        review.Suggestion!.Key.ShouldBe("B");
    }
}
