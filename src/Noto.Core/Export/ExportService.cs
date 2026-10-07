using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noto.Core.Import;
using Noto.Core.Interfaces;
using Noto.Core.Links;
using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.Core.Export;

public sealed record ItemTagRow(Guid ItemId, Guid TagId);

// Everything the user owns. Credentials, connections and link previews are deliberately not part of the schema.
public sealed record ExportDocument(
    int Version,
    DateTimeOffset ExportedAt,
    IReadOnlyList<Workspace> Workspaces,
    IReadOnlyList<TodoItem> Items,
    IReadOnlyList<ItemEvent> Events,
    IReadOnlyList<RecurrenceRule> RecurrenceRules,
    IReadOnlyList<Tag> Tags,
    IReadOnlyList<ItemTagRow> ItemTags,
    IReadOnlyList<DayNote> DayNotes,
    IReadOnlyList<TodoLink> Links);

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ExportDocument))]
public sealed partial class ExportJson : JsonSerializerContext;

public sealed class ExportService(IUnitOfWork uow, IClock clock)
{
    public const int FormatVersion = 1;

    // `workspaceId` null exports every workspace.
    public async Task<string> ExportJsonAsync(Guid? workspaceId = null) =>
        JsonSerializer.Serialize(await BuildAsync(workspaceId), ExportJson.Default.ExportDocument);

    public async Task<string> ExportItemsCsvAsync(Guid? workspaceId = null)
    {
        var doc = await BuildAsync(workspaceId);
        var tagNames = doc.Tags.ToDictionary(t => t.Id, t => t.Name);
        var tagsByItem = doc.ItemTags.ToLookup(t => t.ItemId, t => tagNames.GetValueOrDefault(t.TagId, ""));
        var workspaces = doc.Workspaces.ToDictionary(w => w.Id, w => w.Name);

        var sb = new StringBuilder();
        sb.AppendLine(Csv.Row(["id", "workspace", "parent_id", "title", "notes", "status", "someday", "planned_for", "due_date",
            "estimate_minutes", "priority", "waiting_on", "completed_on", "created_at", "tags"], guardFormulas: false));
        foreach (var i in doc.Items)
        {
            sb.AppendLine(Csv.Row([
                i.Id.ToString(), workspaces.GetValueOrDefault(i.WorkspaceId), i.ParentId?.ToString(), i.Title, i.Notes,
                i.Status.ToString(), i.IsSomeday ? "true" : "false", Date(i.PlannedFor), Date(i.DueDate),
                i.EstimateMinutes?.ToString(), i.Priority.ToString(), i.WaitingOn, Date(i.CompletedOn),
                i.CreatedAt.ToString("O"), string.Join(';', tagsByItem[i.Id])]));
        }
        return sb.ToString();
    }

    static string? Date(DateOnly? d) => d?.ToString("yyyy-MM-dd");

    Task<ExportDocument> BuildAsync(Guid? workspaceId) => uow.RunAsync(async store =>
    {
        var all = await store.Workspaces.ListAsync();
        var workspaces = all.Where(w => workspaceId is null || w.Id == workspaceId).ToList();
        if (workspaceId is not null && workspaces.Count == 0) throw new InvalidOperationException("Workspace not found");

        List<TodoItem> items = [];
        List<ItemEvent> events = [];
        List<RecurrenceRule> rules = [];
        List<Tag> tags = [];
        List<ItemTagRow> itemTags = [];
        List<DayNote> notes = [];
        foreach (var ws in workspaces)
        {
            items.AddRange(await store.Items.ListAsync(ws.Id));
            events.AddRange(await store.Events.ListForWorkspaceAsync(ws.Id));
            rules.AddRange(await store.Rules.ListAsync(ws.Id));
            tags.AddRange(await store.Tags.ListAsync(ws.Id));
            itemTags.AddRange((await store.Tags.ListItemTagsAsync(ws.Id)).Select(t => new ItemTagRow(t.ItemId, t.TagId)));
            notes.AddRange(await store.DayNotes.ListAsync(ws.Id));
        }
        var links = items.Count == 0 ? [] : await store.Links.ListForItemsAsync(items.Select(i => i.Id).ToList());

        return new ExportDocument(FormatVersion, clock.UtcNow, workspaces, items, events, rules, tags, itemTags, notes, links);
    });
}
