namespace Noto.Core.Import;

// Format-neutral shape every importer produces. `Date` is the day to work on it; `Due` is an external deadline.
public sealed record ImportedItem(
    string Title,
    string? Notes = null,
    DateOnly? Date = null,
    DateOnly? Due = null,
    int Priority = 0,
    int? EstimateMinutes = null,
    bool IsDone = false,
    DateOnly? CompletedOn = null,
    bool IsSomeday = false,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<ImportedItem>? Subtasks = null
);

public enum ImportFormat
{
    TodoistCsv,
    TodoistJson,
    ThingsJson,
    RemindersIcs,
    TickTickCsv,
    MarkdownChecklist,
}

public static class Importers
{
    public static IReadOnlyList<ImportedItem> Parse(ImportFormat format, string content) =>
        format switch
        {
            ImportFormat.TodoistCsv => TodoistImporter.ParseCsv(content),
            ImportFormat.TodoistJson => TodoistImporter.ParseJson(content),
            ImportFormat.ThingsJson => ThingsImporter.Parse(content),
            ImportFormat.RemindersIcs => RemindersImporter.Parse(content),
            ImportFormat.TickTickCsv => TickTickImporter.Parse(content),
            _ => MarkdownImporter.Parse(content),
        };
}

public sealed class ImportFormatException(string message) : Exception(message);
