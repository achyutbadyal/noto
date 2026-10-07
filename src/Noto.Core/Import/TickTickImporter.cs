namespace Noto.Core.Import;

// TickTick backup CSV: a few metadata lines, then a header starting with "Folder Name".
public static class TickTickImporter
{
    // TickTick priorities: 0 none, 1 low, 3 medium, 5 high.
    static int Priority(string p) =>
        p switch
        {
            "5" => 3,
            "3" => 2,
            "1" => 1,
            _ => 0,
        };

    public static IReadOnlyList<ImportedItem> Parse(string content)
    {
        var rows = Csv.Parse(content);
        var headerIndex = rows.FindIndex(r => r.Length > 2 && r[0] == "Folder Name");
        if (headerIndex < 0)
            throw new ImportFormatException("Not a TickTick backup (no 'Folder Name' header)");

        var nodes = new Dictionary<string, Node>();
        var order = new List<(string? Parent, Node Node)>();
        foreach (var r in Csv.ParseWithHeader(rows, headerIndex))
        {
            var status = r.GetValueOrDefault("Status") ?? "0";
            if (status == "2")
                continue; // archived

            var title = (r.GetValueOrDefault("Title") ?? "").Trim();
            if (title.Length == 0)
                continue;
            var done = status == "1";

            var node = new Node(
                new ImportedItem(
                    title,
                    ImportHelpers.NullIfBlank(r.GetValueOrDefault("Content")),
                    Date: ImportHelpers.ParseDate(r.GetValueOrDefault("Start Date"))
                        ?? ImportHelpers.ParseDate(r.GetValueOrDefault("Due Date")),
                    Due: ImportHelpers.ParseDate(r.GetValueOrDefault("Due Date")),
                    Priority: Priority(r.GetValueOrDefault("Priority") ?? "0"),
                    IsDone: done,
                    CompletedOn: done
                        ? ImportHelpers.ParseDate(r.GetValueOrDefault("Completed Time"))
                        : null,
                    Tags: ImportHelpers.SplitTags(r.GetValueOrDefault("Tags"))
                )
            );

            var id = ImportHelpers.NullIfBlank(r.GetValueOrDefault("taskId"));
            if (id is not null)
                nodes[id] = node;
            order.Add((ImportHelpers.NullIfBlank(r.GetValueOrDefault("parentId")), node));
        }

        var roots = new List<Node>();
        foreach (var (parent, node) in order)
        {
            if (parent is not null && nodes.TryGetValue(parent, out var p))
                p.Children.Add(node);
            else
                roots.Add(node);
        }
        return roots.Select(n => n.Build()).ToList();
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
