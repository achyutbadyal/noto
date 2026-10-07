using System.Text.Json.Nodes;
using Noto.Core.Models;
using Noto.Core.Sync;

namespace Noto.Sync.Tests;

public class LwwTests
{
    static readonly Guid A = Guid.CreateVersion7();
    static string Stamp(long ms, int counter = 0) => new Hlc(ms, counter, A).ToString();

    static Op Set(string field, JsonNode? value, string hlc) =>
        new(Guid.NewGuid(), EntityTypes.TodoItem, Guid.NewGuid(), Guid.NewGuid(), OpKinds.Set, field, value, hlc, A);

    [Fact]
    public void Newer_set_wins_and_records_its_clock()
    {
        var row = new JsonObject { ["title"] = "old" };
        var clocks = new Dictionary<string, string> { ["title"] = Stamp(1) };

        var outcome = LwwRow.Apply(Set("title", "new", Stamp(2)), row, clocks).Single();

        outcome.Applied.ShouldBeTrue();
        row["title"]!.GetValue<string>().ShouldBe("new");
        clocks["title"].ShouldBe(Stamp(2));
    }

    [Fact]
    public void Older_set_is_ignored_but_still_reported()
    {
        var row = new JsonObject { ["title"] = "kept" };
        var clocks = new Dictionary<string, string> { ["title"] = Stamp(5) };

        var outcome = LwwRow.Apply(Set("title", "stale", Stamp(1)), row, clocks).Single();

        outcome.Applied.ShouldBeFalse();
        row["title"]!.GetValue<string>().ShouldBe("kept");
    }

    [Fact]
    public void Replaying_the_same_op_is_a_no_op()
    {
        var row = new JsonObject();
        var clocks = new Dictionary<string, string>();
        var op = Set("title", "x", Stamp(3));

        LwwRow.Apply(op, row, clocks).Single().Applied.ShouldBeTrue();
        LwwRow.Apply(op, row, clocks).Single().Applied.ShouldBeFalse();
    }

    [Fact]
    public void Different_fields_do_not_interfere()
    {
        var row = new JsonObject();
        var clocks = new Dictionary<string, string>();

        LwwRow.Apply(Set("title", "renamed", Stamp(10)), row, clocks);
        LwwRow.Apply(Set("status", "Done", Stamp(5)), row, clocks); // older overall, different field

        row["title"]!.GetValue<string>().ShouldBe("renamed");
        row["status"]!.GetValue<string>().ShouldBe("Done");
    }

    [Fact]
    public void Insert_expands_into_per_field_sets_and_ignores_id()
    {
        var row = new JsonObject();
        var clocks = new Dictionary<string, string> { ["priority"] = Stamp(9) };
        var insert = new Op(Guid.NewGuid(), EntityTypes.TodoItem, Guid.NewGuid(), Guid.NewGuid(), OpKinds.Insert, null,
            new JsonObject { ["id"] = "x", ["title"] = "t", ["priority"] = 1 }, Stamp(2), A);

        var outcomes = LwwRow.Apply(insert, row, clocks);

        row["title"]!.GetValue<string>().ShouldBe("t");
        row.ContainsKey("id").ShouldBeFalse();
        row.ContainsKey("priority").ShouldBeFalse(); // lost to the newer clock
        outcomes.Count.ShouldBe(2);
    }

    [Fact]
    public void Device_breaks_a_perfect_tie_deterministically()
    {
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var row1 = new JsonObject(); var clocks1 = new Dictionary<string, string>();
        var row2 = new JsonObject(); var clocks2 = new Dictionary<string, string>();
        var a = Set("title", "from-low", new Hlc(5, 0, low).ToString());
        var b = Set("title", "from-high", new Hlc(5, 0, high).ToString());

        LwwRow.Apply(a, row1, clocks1); LwwRow.Apply(b, row1, clocks1);
        LwwRow.Apply(b, row2, clocks2); LwwRow.Apply(a, row2, clocks2);

        row1["title"]!.GetValue<string>().ShouldBe("from-high");
        row2["title"]!.GetValue<string>().ShouldBe("from-high");
    }
}

public class RowNormalizerTests
{
    static JsonObject Row(string status) => new()
    {
        ["id"] = Guid.NewGuid().ToString(), ["status"] = status, ["created_at"] = "2026-10-01T00:00:00.0000000+00:00",
        ["is_someday"] = false, ["is_container"] = false,
    };

    [Fact]
    public void Concurrent_done_and_dropped_merge_into_a_valid_item()
    {
        // status says Dropped, but the Done device's completion fields also survived LWW.
        var row = Row("Dropped");
        row["completed_on"] = "2026-10-07";
        row["completed_at"] = "2026-10-07T10:00:00.0000000+00:00";

        RowNormalizer.Item(row);

        var item = SyncRows.ToItem(row);
        ItemInvariants.Check(item).ShouldBeEmpty();
        item.Status.ShouldBe(ItemStatus.Dropped);
        item.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void Done_without_completion_fields_gets_them_filled_deterministically()
    {
        var row = Row("Done");
        RowNormalizer.Item(row);
        ItemInvariants.Check(SyncRows.ToItem(row)).ShouldBeEmpty();
    }

    [Fact]
    public void Waiting_without_waiting_on_falls_back_to_open()
    {
        var row = Row("Waiting");
        RowNormalizer.Item(row);
        row["status"]!.GetValue<string>().ShouldBe("Open");
    }

    [Fact]
    public void Someday_and_planned_together_resolve_to_planned()
    {
        var row = Row("Open");
        row["is_someday"] = true;
        row["planned_for"] = "2026-10-08";
        RowNormalizer.Item(row);
        ItemInvariants.Check(SyncRows.ToItem(row)).ShouldBeEmpty();
    }

    [Fact]
    public void Is_idempotent_and_leaves_valid_rows_alone()
    {
        var row = Row("Done");
        row["completed_on"] = "2026-10-07";
        row["completed_at"] = "2026-10-07T10:00:00.0000000+00:00";
        var completedAt = row["completed_at"]!.GetValue<string>();

        RowNormalizer.Item(row);
        var once = row.ToJsonString();
        RowNormalizer.Item(row);

        row.ToJsonString().ShouldBe(once);
        row["completed_at"]!.GetValue<string>().ShouldBe(completedAt);
        row["status"]!.GetValue<string>().ShouldBe("Done");
    }
}
