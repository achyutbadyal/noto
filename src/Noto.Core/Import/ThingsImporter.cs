using System.Text.Json.Nodes;

namespace Noto.Core.Import;

// Things 3 JSON (as produced by exporters over its database): title, notes, status, start, start_date, deadline, stop_date, tags, checklist.
public static class ThingsImporter
{
    public static IReadOnlyList<ImportedItem> Parse(string content)
    {
        var root = JsonNode.Parse(content) ?? throw new ImportFormatException("Empty JSON");
        var array =
            root as JsonArray
            ?? root["items"] as JsonArray
            ?? root["todos"] as JsonArray
            ?? throw new ImportFormatException("Expected an array of to-dos");

        var items = new List<ImportedItem>();
        foreach (var o in array.OfType<JsonObject>())
        {
            var status = o["status"]?.GetValue<string>() ?? "open";
            if (status.Equals("canceled", StringComparison.OrdinalIgnoreCase))
                continue;

            var done = status.Equals("completed", StringComparison.OrdinalIgnoreCase);
            var title = (o["title"]?.GetValue<string>() ?? "").Trim();
            if (title.Length == 0)
                continue;

            var checklist = o["checklist"]
                ?.AsArray()
                .OfType<JsonObject>()
                .Select(c => new ImportedItem(
                    (c["title"]?.GetValue<string>() ?? "").Trim(),
                    IsDone: c["status"]
                        ?.GetValue<string>()
                        ?.Equals("completed", StringComparison.OrdinalIgnoreCase) == true
                ))
                .Where(c => c.Title.Length > 0)
                .ToList();

            items.Add(
                new ImportedItem(
                    title,
                    ImportHelpers.NullIfBlank(o["notes"]?.GetValue<string>()),
                    Date: ImportHelpers.ParseDate(o["start_date"]?.GetValue<string>()),
                    Due: ImportHelpers.ParseDate(o["deadline"]?.GetValue<string>()),
                    IsDone: done,
                    CompletedOn: done
                        ? ImportHelpers.ParseDate(o["stop_date"]?.GetValue<string>())
                        : null,
                    IsSomeday: !done
                        && (
                            o["start"]
                                ?.GetValue<string>()
                                ?.Equals("Someday", StringComparison.OrdinalIgnoreCase)
                            ?? false
                        ),
                    Tags: o["tags"]?.AsArray().Select(t => t!.GetValue<string>()).ToList(),
                    Subtasks: checklist is { Count: > 0 } ? checklist : null
                )
            );
        }
        return items;
    }
}
