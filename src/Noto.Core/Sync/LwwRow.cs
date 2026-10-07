using System.Text.Json.Nodes;

namespace Noto.Core.Sync;

public sealed record FieldOutcome(string Field, JsonNode? Previous, bool Applied);

// Per-field last-writer-wins by HLC, shared by clients and the server so both converge on the same rule.
public static class LwwRow
{
    // Applies a `set` or an `insert` (a set of every field) onto `row`, updating `clocks` for winning fields.
    public static IReadOnlyList<FieldOutcome> Apply(Op op, JsonObject row, IDictionary<string, string> clocks)
    {
        var outcomes = new List<FieldOutcome>();
        if (op.Kind == OpKinds.Set)
        {
            if (op.Field is { } f) outcomes.Add(ApplyField(f, op.Value, op.Hlc, row, clocks));
        }
        else if (op.Value is JsonObject values)
        {
            foreach (var (field, value) in values)
                if (field != "id") outcomes.Add(ApplyField(field, value, op.Hlc, row, clocks));
        }
        return outcomes;
    }

    static FieldOutcome ApplyField(string field, JsonNode? value, string hlc, JsonObject row, IDictionary<string, string> clocks)
    {
        var previous = row[field]?.DeepClone();
        clocks.TryGetValue(field, out var current);
        if (Hlc.Compare(hlc, current) <= 0) return new FieldOutcome(field, previous, false);

        row[field] = value?.DeepClone();
        clocks[field] = hlc;
        return new FieldOutcome(field, previous, true);
    }
}
