using Noto.Core.Recurrence;

namespace Noto.Core.Models;

public static class ItemInvariants
{
    // I1–I7 from docs/04 §2.1. `parent` is required only to check I5.
    public static IReadOnlyList<string> Check(TodoItem i, TodoItem? parent = null)
    {
        var errors = new List<string>();
        void Require(bool ok, string message) { if (!ok) errors.Add(message); }

        Require(!i.IsSomeday || (i.PlannedFor is null && i.Status == ItemStatus.Open), "I1: someday items must be open and unplanned");
        Require((i.Status == ItemStatus.Done) == (i.CompletedOn is not null && i.CompletedAt is not null), "I2: done ⇔ completed_on and completed_at set");
        Require((i.Status == ItemStatus.Dropped) == (i.DroppedAt is not null), "I3: dropped ⇔ dropped_at set");
        Require(i.Status != ItemStatus.Waiting || i.WaitingOn is not null, "I4: waiting requires waiting_on");
        Require(i.ParentId is null || (parent is { IsContainer: true, ParentId: null } && parent.Id == i.ParentId), "I5: parent must be a top-level container");
        Require(!i.IsContainer || i.PlannedFor is null, "I6: containers are never planned");
        Require((i.RecurrenceRuleId is null) == (i.OccurrenceDate is null), "I7: recurrence rule and occurrence date go together");
        if (i.RecurrenceRuleId is { } rule && i.OccurrenceDate is { } day)
            Require(i.Id == Uuid5.ForOccurrence(rule, day), "I7: recurring id must be UUIDv5(rule, date)");

        return errors;
    }
}
