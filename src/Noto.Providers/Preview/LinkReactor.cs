using System.Text.Json.Nodes;
using Noto.Core.Commands;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Recurrence;
using Noto.Core.Time;

namespace Noto.Providers.Preview;

public sealed class ReactorOptions
{
    // Auto-complete is opt-in per workspace (docs/10 principle 5); everything else is a suggestion.
    public HashSet<Guid> AutoCompleteWorkspaces { get; init; } = [];
}

public sealed record ReactionOutcome(
    IReadOnlyList<LinkSuggestion> Suggestions,
    IReadOnlyList<LinkSuggestion> Applied
);

// Applies live-link rules to the items that link to a changed URL, recording LinkStateChanged events.
public sealed class LinkReactor(
    IUnitOfWork uow,
    ICommandBus bus,
    IClock clock,
    Guid deviceId,
    ReactorOptions? options = null
)
{
    readonly ReactorOptions _options = options ?? new();

    public async Task<ReactionOutcome> ReactAsync(IReadOnlyList<LinkChange> changes)
    {
        List<LinkSuggestion> pending = [],
            applied = [];

        foreach (var change in changes)
        {
            var items = await uow.RunAsync(async s =>
            {
                var list = new List<TodoItem>();
                foreach (var id in await s.Links.ListItemIdsForUrlAsync(change.Url))
                    if (await s.Items.GetAsync(id) is { DeletedAt: null } item)
                        list.Add(item);
                return list;
            });

            foreach (var item in items)
            {
                await RecordEventAsync(item, change);
                foreach (var suggestion in LiveLinkRules.Evaluate(item, change))
                {
                    if (await TryApplyAsync(item, suggestion))
                        applied.Add(suggestion);
                    else
                        pending.Add(suggestion);
                }
            }
        }
        return new ReactionOutcome(pending, applied);
    }

    async Task<bool> TryApplyAsync(TodoItem item, LinkSuggestion s)
    {
        switch (s.Kind)
        {
            case SuggestionKind.ReturnFromWaiting:
                await bus.SendAsync(new EndWaiting(item.Id, "link"));
                await bus.SendAsync(new PlanItem(item.Id, null, PlanKind.KeepToday));
                return true;
            case SuggestionKind.MarkDone
                when _options.AutoCompleteWorkspaces.Contains(item.WorkspaceId):
                await bus.SendAsync(new CompleteItem(item.Id));
                return true;
            default:
                return false;
        }
    }

    // Deterministic id: every device observing the same change writes the same event, which collapses on sync.
    Task RecordEventAsync(TodoItem item, LinkChange change) =>
        uow.RunAsync(async s =>
        {
            var id = Uuid5.Create(item.Id, $"{change.Url}|{change.ToHash}");
            if ((await s.Events.ListForItemAsync(item.Id)).Any(e => e.Id == id))
                return 0;

            var ws = await s.Workspaces.GetAsync(item.WorkspaceId);
            await s.Events.AppendAsync(
                new ItemEvent
                {
                    Id = id,
                    ItemId = item.Id,
                    WorkspaceId = item.WorkspaceId,
                    Type = ItemEventType.LinkStateChanged,
                    Data = new JsonObject
                    {
                        ["url"] = change.Url,
                        ["provider"] = change.Preview.ProviderId,
                        ["from_state"] = change.From?.ToString(),
                        ["to_state"] = change.To?.ToString(),
                    },
                    OccurredAt = clock.UtcNow,
                    Tz = ws is null ? "UTC" : LogicalDate.EffectiveZone(ws, clock).Id,
                    DeviceId = deviceId,
                }
            );
            return 0;
        });
}
