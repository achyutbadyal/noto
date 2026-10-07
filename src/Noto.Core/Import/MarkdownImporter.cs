using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Noto.Core.Import;

// `- [ ] Title #tag due:2026-10-10` checklists; nested lists become subtasks. Plain bullets are ignored.
public static partial class MarkdownImporter
{
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseTaskLists().Build();

    [GeneratedRegex(@"(?:^|\s)due:(?<d>\d{4}-\d{2}-\d{2})")]
    private static partial Regex DueToken();

    [GeneratedRegex(@"(?:^|\s)#(?<t>[\w-]+)")]
    private static partial Regex TagToken();

    public static IReadOnlyList<ImportedItem> Parse(string content) => Read(Markdown.Parse(content, Pipeline));

    static List<ImportedItem> Read(ContainerBlock container)
    {
        var items = new List<ImportedItem>();
        foreach (var list in container.OfType<ListBlock>())
            foreach (var li in list.OfType<ListItemBlock>())
            {
                var children = Read(li);
                var inline = li.OfType<ParagraphBlock>().FirstOrDefault()?.Inline;
                if (inline?.FirstChild is TaskList task)
                {
                    var text = string.Concat(inline.Descendants<LiteralInline>().Select(l => l.Content.ToString()));
                    var item = ToItem(text, task.Checked);
                    items.Add(children.Count == 0 ? item : item with { Subtasks = children });
                }
                else items.AddRange(children); // a plain bullet: keep any checklist beneath it
            }
        return items;
    }

    static ImportedItem ToItem(string text, bool done)
    {
        var due = DueToken().Match(text) is { Success: true } d ? DateOnly.ParseExact(d.Groups["d"].Value, "yyyy-MM-dd") : (DateOnly?)null;
        var tags = TagToken().Matches(text).Select(t => t.Groups["t"].Value).ToList();
        var title = DueToken().Replace(TagToken().Replace(text, ""), "").Trim();
        return new ImportedItem(title, Due: due, IsDone: done, Tags: tags);
    }
}
