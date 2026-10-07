using System.Text.Json.Nodes;

namespace Noto.Core.Import;

// Todoist CSV template export (TYPE, CONTENT, DESCRIPTION, PRIORITY, INDENT, DATE …) and API/sync JSON.
public static class TodoistImporter
{
    // CSV priority 1 is p1 (highest); API priority 4 is urgent. Both map onto Noto's 0–4.
    static int FromCsvPriority(string p) =>
        p switch
        {
            "1" => 4,
            "2" => 3,
            "3" => 2,
            _ => 0,
        };

    static int FromApiPriority(int p) =>
        p switch
        {
            4 => 4,
            3 => 3,
            2 => 2,
            _ => 0,
        };

    public static IReadOnlyList<ImportedItem> ParseCsv(string content)
    {
        var rows = Csv.Parse(content);
        var headerIndex = rows.FindIndex(r =>
            r.Length > 1 && r[0].Equals("TYPE", StringComparison.OrdinalIgnoreCase)
        );
        if (headerIndex < 0)
            throw new ImportFormatException("Not a Todoist CSV export (no TYPE header)");

        var roots = new List<Node>();
        var stack = new List<Node>(); // open ancestors by indent
        foreach (var r in Csv.ParseWithHeader(rows, headerIndex))
        {
            if (!r["TYPE"].Equals("task", StringComparison.OrdinalIgnoreCase))
                continue;
            var node = new Node(
                new ImportedItem(
                    r["CONTENT"].Trim(),
                    ImportHelpers.NullIfBlank(r.GetValueOrDefault("DESCRIPTION")),
                    Date: ImportHelpers.ParseDate(r.GetValueOrDefault("DATE")),
                    Priority: FromCsvPriority(r.GetValueOrDefault("PRIORITY") ?? "4")
                )
            );

            var indent = int.TryParse(r.GetValueOrDefault("INDENT"), out var n)
                ? Math.Max(1, n)
                : 1;
            while (stack.Count >= indent)
                stack.RemoveAt(stack.Count - 1);
            if (stack.Count == 0)
                roots.Add(node);
            else
                stack[^1].Children.Add(node);
            stack.Add(node);
        }
        return roots.Select(n => n.Build()).Where(i => i.Title.Length > 0).ToList();
    }

    public static IReadOnlyList<ImportedItem> ParseJson(string content)
    {
        var root = JsonNode.Parse(content) ?? throw new ImportFormatException("Empty JSON");
        var array =
            root as JsonArray
            ?? root["items"] as JsonArray
            ?? throw new ImportFormatException("Expected an array of items");

        var nodes = new Dictionary<string, Node>();
        var order = new List<(string? Id, string? Parent, Node Node)>();
        foreach (var o in array.OfType<JsonObject>())
        {
            var due = o["due"]?["date"]?.GetValue<string>();
            var done =
                o["checked"] is { } c
                && (
                    c.GetValueKind() == System.Text.Json.JsonValueKind.True
                    || (
                        c.GetValueKind() == System.Text.Json.JsonValueKind.Number
                        && c.GetValue<int>() != 0
                    )
                );
            var item = new ImportedItem(
                (o["content"]?.GetValue<string>() ?? "").Trim(),
                ImportHelpers.NullIfBlank(o["description"]?.GetValue<string>()),
                Date: ImportHelpers.ParseDate(due),
                Priority: FromApiPriority(o["priority"]?.GetValue<int>() ?? 1),
                IsDone: done,
                CompletedOn: ImportHelpers.ParseDate(o["completed_at"]?.GetValue<string>()),
                Tags: o["labels"]?.AsArray().Select(l => l!.GetValue<string>()).ToList()
            );
            var node = new Node(item);
            var id = o["id"]?.ToString();
            if (id is not null)
                nodes[id] = node;
            order.Add((id, o["parent_id"]?.ToString(), node));
        }

        var roots = new List<Node>();
        foreach (var (_, parent, node) in order)
        {
            if (parent is not null && nodes.TryGetValue(parent, out var p))
                p.Children.Add(node);
            else
                roots.Add(node);
        }
        return roots.Select(n => n.Build()).Where(i => i.Title.Length > 0).ToList();
    }

    sealed class Node(ImportedItem item)
    {
        public List<Node> Children { get; } = [];

        public ImportedItem Build() =>
            Children.Count == 0
                ? item
                : item with
                {
                    Subtasks = Children.Select(c => c.Build()).ToList(),
                };
    }
}
