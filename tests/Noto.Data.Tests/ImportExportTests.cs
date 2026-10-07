using System.Text.Json;
using Noto.Core.Commands;
using Noto.Core.Export;
using Noto.Core.Import;
using Noto.Core.Insights;
using Noto.Core.Layouts;
using Noto.Core.Models;

namespace Noto.Data.Tests;

public class ContainerServiceTests : IDisposable
{
    readonly FeatureFixture _f = new();
    readonly ContainerService _svc;

    public ContainerServiceTests() => _svc = new ContainerService(_f.Uow, _f.Bus);

    public void Dispose() => _f.Dispose();

    async Task<(Guid Parent, List<Guid> Kids)> Setup(params string[] titles)
    {
        var parent = await _f.CreateItemAsync("Ship v2", new DateOnly(2026, 10, 7));
        await _f.Bus.SendAsync(new BreakDown(parent, titles));
        var kids = (await _f.Uow.RunAsync(s => s.Items.ListAsync(_f.Ws.Id))).Where(i => i.ParentId == parent).OrderBy(i => i.Title).Select(i => i.Id).ToList();
        return (parent, kids);
    }

    [Fact]
    public async Task Completing_the_last_open_child_completes_the_parent_with_an_undo_token()
    {
        var (parent, kids) = await Setup("a", "b");

        await _f.Bus.SendAsync(new CompleteItem(kids[0]));
        (await _svc.AfterChildResolvedAsync(kids[0])).Outcome.ShouldBe(ContainerOutcome.StillOpen);

        await _f.Bus.SendAsync(new CompleteItem(kids[1]));
        var follow = await _svc.AfterChildResolvedAsync(kids[1]);

        follow.Outcome.ShouldBe(ContainerOutcome.AutoComplete);
        (await _f.LoadAsync(parent)).Status.ShouldBe(ItemStatus.Done);

        await _f.Bus.UndoAsync(follow.AutoCompleted!.UndoToken);
        (await _f.LoadAsync(parent)).Status.ShouldBe(ItemStatus.Open);
    }

    [Fact]
    public async Task All_children_dropped_asks_instead_of_deciding()
    {
        var (parent, kids) = await Setup("a", "b");
        foreach (var k in kids) await _f.Bus.SendAsync(new DropItem(k, DropReason.NotNeeded));

        var follow = await _svc.AfterChildResolvedAsync(kids[1]);

        (follow.Outcome, follow.AutoCompleted).ShouldBe((ContainerOutcome.AskDoneOrDrop, null));
        (await _f.LoadAsync(parent)).Status.ShouldBe(ItemStatus.Open);
    }

    [Fact]
    public async Task Non_children_are_ignored()
    {
        var solo = await _f.CreateItemAsync("solo");
        (await _svc.AfterChildResolvedAsync(solo)).Outcome.ShouldBe(ContainerOutcome.StillOpen);
    }
}

public class ImportServiceTests : IDisposable
{
    readonly FeatureFixture _f = new();
    readonly ImportService _svc;
    static readonly DateOnly Today = new(2026, 10, 7);

    public ImportServiceTests() => _svc = new ImportService(_f.Bus, _f.Uow, _f.Clock);

    public void Dispose() => _f.Dispose();

    Task<IReadOnlyList<TodoItem>> Items() => _f.Uow.RunAsync(s => s.Items.ListAsync(_f.Ws.Id));

    [Fact]
    public async Task Open_items_start_with_carry_zero_even_when_dated_in_the_past()
    {
        var result = await _svc.ImportAsync(_f.Ws.Id,
        [
            new ImportedItem("Overdue elsewhere", Date: new DateOnly(2026, 9, 1)),
            new ImportedItem("Next week", Date: new DateOnly(2026, 10, 14)),
            new ImportedItem("No date"),
        ]);

        result.Created.ShouldBe(3);
        var items = (await Items()).ToDictionary(i => i.Title);
        items["Overdue elsewhere"].PlannedFor.ShouldBe(Today);
        items["Next week"].PlannedFor.ShouldBe(new DateOnly(2026, 10, 14));
        items["No date"].PlannedFor.ShouldBeNull();

        var metrics = await _f.Derive.GetMetricsAsync(_f.Ws.Id, items.Values.ToList());
        metrics.Values.ShouldAllBe(m => m.Carry == 0 && m.Age == 0);
    }

    [Fact]
    public async Task Fields_tags_someday_and_completion_are_carried_over()
    {
        await _svc.ImportAsync(_f.Ws.Id,
        [
            new ImportedItem("Rich", Notes: "n", Due: new DateOnly(2026, 10, 20), Priority: 3, Tags: ["Home", "home", "errand"]),
            new ImportedItem("Maybe", IsSomeday: true),
            new ImportedItem("Finished", IsDone: true, CompletedOn: new DateOnly(2026, 10, 2)),
            new ImportedItem("Future finish", IsDone: true, CompletedOn: new DateOnly(2030, 1, 1)),
        ]);

        var items = (await Items()).ToDictionary(i => i.Title);
        var rich = items["Rich"];
        (rich.Notes, rich.DueDate, rich.Priority).ShouldBe(("n", new DateOnly(2026, 10, 20), 3));
        (await _f.Uow.RunAsync(s => s.Tags.GetItemTagIdsAsync(rich.Id))).Count.ShouldBe(2);
        (await _f.Uow.RunAsync(s => s.Tags.ListAsync(_f.Ws.Id))).Count.ShouldBe(2);
        items["Maybe"].IsSomeday.ShouldBeTrue();
        (items["Finished"].Status, items["Finished"].CompletedOn).ShouldBe((ItemStatus.Done, new DateOnly(2026, 10, 2)));
        items["Future finish"].CompletedOn.ShouldBe(Today);
    }

    [Fact]
    public async Task Two_to_five_subtasks_become_real_subtasks_including_completed_ones()
    {
        await _svc.ImportAsync(_f.Ws.Id,
        [
            new ImportedItem("Trip", Date: Today, Subtasks: [new("Passport", IsDone: true), new("Tickets")]),
        ]);

        var items = await Items();
        var trip = items.Single(i => i.Title == "Trip");
        trip.IsContainer.ShouldBeTrue();
        var kids = items.Where(i => i.ParentId == trip.Id).ToDictionary(i => i.Title);
        kids["Passport"].Status.ShouldBe(ItemStatus.Done);
        (kids["Tickets"].Status, kids["Tickets"].PlannedFor).ShouldBe((ItemStatus.Open, (DateOnly?)null)); // only the first child inherits the plan
    }

    [Fact]
    public async Task Other_subtask_counts_are_flattened_with_the_parent_in_the_title()
    {
        await _svc.ImportAsync(_f.Ws.Id, [new ImportedItem("Parent", Subtasks: [new("Only child")])]);
        (await Items()).Select(i => i.Title).Order().ShouldBe(["Parent", "Parent › Only child"]);
    }

    [Fact]
    public async Task Bad_rows_are_reported_and_do_not_stop_the_import()
    {
        var result = await _svc.ImportAsync(_f.Ws.Id, [new ImportedItem("  "), new ImportedItem("Good")]);

        result.Created.ShouldBe(1);
        result.Errors.ShouldHaveSingleItem();
        (await Items()).Select(i => i.Title).ShouldBe(["Good"]);
    }

    [Fact]
    public async Task Imports_write_events_with_an_import_source()
    {
        await _svc.ImportAsync(_f.Ws.Id, [new ImportedItem("x")]);
        var id = (await Items()).Single().Id;
        (await _f.Uow.RunAsync(s => s.Events.ListForItemAsync(id))).First().Data!["source"]!.GetValue<string>().ShouldBe("import");
    }

    [Fact]
    public async Task End_to_end_from_a_markdown_checklist()
    {
        var parsed = Importers.Parse(ImportFormat.MarkdownChecklist, "- [ ] Milk #home\n- [x] Bread\n");
        var result = await _svc.ImportAsync(_f.Ws.Id, parsed);
        (result.Created, result.Completed).ShouldBe((2, 1));
    }
}

public class ExportServiceTests : IDisposable
{
    readonly FeatureFixture _f = new();
    readonly ExportService _svc;
    readonly Guid _item;

    public ExportServiceTests()
    {
        _svc = new ExportService(_f.Uow, _f.Clock);
        _item = _f.CreateItemAsync("=cmd|calc, \"quoted\"", new DateOnly(2026, 10, 7)).GetAwaiter().GetResult();
        _f.Bus.SendAsync(new SetNotes(_item, "line1\nline2")).GetAwaiter().GetResult();
        _f.Uow.RunAsync(async s =>
        {
            var tag = new Tag { Id = Guid.CreateVersion7(), WorkspaceId = _f.Ws.Id, Name = "home" };
            await s.Tags.UpsertAsync(tag);
            await s.Tags.SetItemTagsAsync(_item, [tag.Id]);
            await s.DayNotes.UpsertAsync(new DayNote { Id = Guid.CreateVersion7(), WorkspaceId = _f.Ws.Id, Day = new DateOnly(2026, 10, 7), Text = "n" });
            return 0;
        }).GetAwaiter().GetResult();
    }

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Json_contains_everything_the_user_owns()
    {
        using var doc = JsonDocument.Parse(await _svc.ExportJsonAsync());
        var root = doc.RootElement;

        root.GetProperty("Version").GetInt32().ShouldBe(1);
        root.GetProperty("Workspaces").GetArrayLength().ShouldBe(1);
        var item = root.GetProperty("Items").EnumerateArray().Single();
        item.GetProperty("Title").GetString().ShouldBe("=cmd|calc, \"quoted\"");
        item.GetProperty("Status").GetString().ShouldBe("Open"); // enums as names, not numbers
        root.GetProperty("Events").GetArrayLength().ShouldBe(2);
        root.GetProperty("Tags").GetArrayLength().ShouldBe(1);
        root.GetProperty("ItemTags").GetArrayLength().ShouldBe(1);
        root.GetProperty("DayNotes").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Json_has_no_credential_or_preview_sections()
    {
        var json = await _svc.ExportJsonAsync();
        var props = JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToList();

        props.ShouldBe(["Version", "ExportedAt", "Workspaces", "Items", "Events", "RecurrenceRules", "Tags", "ItemTags", "DayNotes", "Links"]);
        json.ToLowerInvariant().ShouldNotContain("token");
        json.ToLowerInvariant().ShouldNotContain("preview");
        json.ToLowerInvariant().ShouldNotContain("secret");
    }

    [Fact]
    public async Task Json_can_be_scoped_to_one_workspace()
    {
        var other = await _f.Workspaces.CreateAsync("Other", "", "", Noto.Core.Presets.BuiltInPresets.Zen);
        await _f.Bus.SendAsync(new CreateItem(Guid.CreateVersion7(), other.Id, "elsewhere"));

        using var all = JsonDocument.Parse(await _svc.ExportJsonAsync());
        using var one = JsonDocument.Parse(await _svc.ExportJsonAsync(_f.Ws.Id));

        all.RootElement.GetProperty("Items").GetArrayLength().ShouldBe(2);
        one.RootElement.GetProperty("Items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Csv_escapes_and_neutralises_formulas()
    {
        var rows = Csv.Parse(await _svc.ExportItemsCsvAsync());

        rows.Count.ShouldBe(2);
        rows[0][0].ShouldBe("id");
        var row = rows[1];
        row[3].ShouldBe("'=cmd|calc, \"quoted\"");
        row[4].ShouldBe("line1\nline2");
        (row[1], row[5], row[7], row[14]).ShouldBe(("Work", "Open", "2026-10-07", "home"));
    }

    [Fact]
    public async Task Unknown_workspace_is_an_error()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => _svc.ExportJsonAsync(Guid.NewGuid()));
    }
}

public class InsightsServiceTests : IDisposable
{
    readonly FeatureFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Service_loads_history_and_reports_stale_and_oldest_items()
    {
        var id = await _f.CreateItemAsync("ancient", new DateOnly(2026, 10, 1));
        var svc = new InsightsService(_f.Uow, _f.Clock, _f.Derive);

        var report = await svc.GetAsync(_f.Ws.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7));

        report.SizeVsCompletion.ShouldBeNull(); // below the sample threshold
        report.OldestOpen.ShouldHaveSingleItem().Item.Id.ShouldBe(id);
    }

    [Fact]
    public async Task Weekly_review_service_combines_wins_sweep_and_next_weeks_outcomes()
    {
        var done = await _f.CreateItemAsync("done thing", new DateOnly(2026, 10, 7));
        await _f.Bus.SendAsync(new CompleteItem(done));
        var maybe = Guid.CreateVersion7();
        await _f.Bus.SendAsync(new CreateItem(maybe, _f.Ws.Id, "someday thing", IsSomeday: true));
        var notes = new Noto.Core.Workspaces.DayNoteService(_f.Uow);
        await notes.SetOutcomesAsync(_f.Ws.Id, new DateOnly(2026, 10, 12), ["Ship it"]);

        var review = await new WeeklyReviewService(new InsightsService(_f.Uow, _f.Clock, _f.Derive), notes)
            .GetAsync(_f.Ws.Id, new DateOnly(2026, 10, 7));

        review.WeekStart.ShouldBe(new DateOnly(2026, 10, 5));
        review.Wins.ShouldHaveSingleItem().Item.Id.ShouldBe(done);
        review.SomedaySweep.ShouldHaveSingleItem().Item.Id.ShouldBe(maybe);
        review.NextWeekOutcomes.ShouldBe(["Ship it"]);
    }
}
