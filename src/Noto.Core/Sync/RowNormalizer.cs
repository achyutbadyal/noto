using System.Text.Json.Nodes;
using Noto.Core.Models;

namespace Noto.Core.Sync;

// Per-field LWW can merge two valid edits into a state that breaks I1–I6 (e.g. Done on one device,
// Dropped on another). This deterministic repair runs on every replica after a merge, so all of them
// still converge, and clocks are untouched.
public static class RowNormalizer
{
    public static void Item(JsonObject r)
    {
        var status = r["status"]?.GetValue<string>() ?? nameof(ItemStatus.Open);

        if (status == nameof(ItemStatus.Waiting) && r["waiting_on"] is null)
            status = nameof(ItemStatus.Open);

        if (status == nameof(ItemStatus.Done))
        {
            r["completed_at"] ??= r["created_at"]?.DeepClone();
            r["completed_on"] ??= r["completed_at"]?.GetValue<string>() is { } at ? at[..10] : null;
        }
        else
        {
            r["completed_at"] = null;
            r["completed_on"] = null;
        }

        if (status == nameof(ItemStatus.Dropped))
            r["dropped_at"] ??= r["created_at"]?.DeepClone();
        else
            r["dropped_at"] = null;

        if (status != nameof(ItemStatus.Open) || r["planned_for"] is not null)
            r["is_someday"] = false;
        if (r["is_container"]?.GetValue<bool>() == true)
            r["planned_for"] = null;

        r["status"] = status;
    }
}
