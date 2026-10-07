using Noto.Core.Models;
using Noto.Core.Recurrence;

namespace Noto.Core.Tests;

public class InvariantTests
{
    [Fact]
    public void A_fresh_item_is_valid() => ItemInvariants.Check(Make.Item()).ShouldBeEmpty();

    [Fact]
    public void I1_someday_must_be_open_and_unplanned()
    {
        var item = Make.Item();
        item.IsSomeday = true;
        item.PlannedFor = new DateOnly(2026, 10, 7);
        ItemInvariants.Check(item).ShouldHaveSingleItem().ShouldStartWith("I1");
    }

    [Fact]
    public void I2_done_requires_completion_fields()
    {
        var item = Make.Item();
        item.Status = ItemStatus.Done;
        ItemInvariants.Check(item).ShouldHaveSingleItem().ShouldStartWith("I2");
    }

    [Fact]
    public void I2_completion_fields_require_done()
    {
        var item = Make.Item();
        item.CompletedOn = new DateOnly(2026, 10, 7);
        item.CompletedAt = DateTimeOffset.UtcNow;
        ItemInvariants.Check(item).ShouldHaveSingleItem().ShouldStartWith("I2");
    }

    [Fact]
    public void I3_dropped_requires_dropped_at()
    {
        var item = Make.Item();
        item.Status = ItemStatus.Dropped;
        ItemInvariants.Check(item).ShouldHaveSingleItem().ShouldStartWith("I3");
    }

    [Fact]
    public void I4_waiting_requires_waiting_on()
    {
        var item = Make.Item();
        item.Status = ItemStatus.Waiting;
        ItemInvariants.Check(item).ShouldHaveSingleItem().ShouldStartWith("I4");
    }

    [Fact]
    public void I5_parent_must_be_a_top_level_container()
    {
        var parent = Make.Item();
        var child = Make.Item();
        child.ParentId = parent.Id;
        ItemInvariants.Check(child, parent).ShouldHaveSingleItem().ShouldStartWith("I5");

        parent.IsContainer = true;
        ItemInvariants.Check(child, parent).ShouldBeEmpty();

        parent.ParentId = Guid.CreateVersion7();
        ItemInvariants.Check(child, parent).ShouldHaveSingleItem().ShouldStartWith("I5");
    }

    [Fact]
    public void I6_containers_are_never_planned()
    {
        var item = Make.Item();
        item.IsContainer = true;
        item.PlannedFor = new DateOnly(2026, 10, 7);
        ItemInvariants.Check(item).ShouldHaveSingleItem().ShouldStartWith("I6");
    }

    [Fact]
    public void I7_recurring_items_need_both_fields_and_a_deterministic_id()
    {
        var rule = Guid.CreateVersion7();
        var day = new DateOnly(2026, 10, 7);
        var item = Make.Item();
        item.RecurrenceRuleId = rule;
        ItemInvariants.Check(item).ShouldHaveSingleItem().ShouldStartWith("I7");

        item.OccurrenceDate = day;
        ItemInvariants.Check(item).ShouldHaveSingleItem().ShouldContain("UUIDv5");

        var valid = new TodoItem
        {
            Id = Uuid5.ForOccurrence(rule, day), WorkspaceId = item.WorkspaceId, Title = "x",
            RecurrenceRuleId = rule, OccurrenceDate = day, CreatedTz = "UTC",
        };
        ItemInvariants.Check(valid).ShouldBeEmpty();
    }
}
