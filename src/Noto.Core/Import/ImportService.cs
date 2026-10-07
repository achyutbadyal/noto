using Noto.Core.Commands;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.Core.Import;

public sealed record ImportResult(int Created, int Completed, IReadOnlyList<string> Errors);

// Writes parsed items through the command bus. Open items start with carry 0: nothing is planned in the past
// and CreatedAt is now, so history from another app isn't held against the user.
public sealed class ImportService(ICommandBus bus, IUnitOfWork uow, IClock clock)
{
    sealed class Tally
    {
        public int Created,
            Completed;
    }

    public async Task<ImportResult> ImportAsync(Guid workspaceId, IReadOnlyList<ImportedItem> items)
    {
        var ws =
            await uow.RunAsync(s => s.Workspaces.GetAsync(workspaceId))
            ?? throw new CommandException("Workspace not found");
        var today = LogicalDate.Today(ws, clock);
        var tally = new Tally();
        var errors = new List<string>();

        foreach (var item in items)
        {
            try
            {
                await ImportOneAsync(ws.Id, today, item, item.Title, tally);
            }
            catch (Exception e) when (e is CommandException or InvariantViolationException)
            {
                errors.Add($"{item.Title}: {e.Message}");
            }
        }
        return new ImportResult(tally.Created, tally.Completed, errors);
    }

    async Task ImportOneAsync(
        Guid workspaceId,
        DateOnly today,
        ImportedItem item,
        string title,
        Tally tally
    )
    {
        var id = Guid.CreateVersion7();
        var planned =
            item.IsDone || item.IsSomeday ? null
            : item.Date is { } d ? (d < today ? today : d)
            : (DateOnly?)null;

        await bus.SendAsync(
            new CreateItem(
                id,
                workspaceId,
                title,
                planned,
                item.IsSomeday && !item.IsDone,
                item.EstimateMinutes,
                "import"
            )
        );
        tally.Created++;

        if (item.Notes is not null)
            await bus.SendAsync(new SetNotes(id, item.Notes));
        if (item.Priority > 0)
            await bus.SendAsync(new SetPriority(id, item.Priority));
        if (item.Due is not null)
            await bus.SendAsync(new SetDueDate(id, item.Due));
        if (item.Tags is { Count: > 0 })
            await ApplyTagsAsync(workspaceId, id, item.Tags);

        if (item.IsDone)
        {
            var on = item.CompletedOn is { } c && c <= today ? c : today;
            await bus.SendAsync(new CompleteItem(id, on));
            tally.Completed++;
            return;
        }

        await ImportSubtasksAsync(workspaceId, today, id, item, tally);
    }

    // 2–5 subtasks become real subtasks; any other count is flattened to "Parent › Child" items.
    async Task ImportSubtasksAsync(
        Guid workspaceId,
        DateOnly today,
        Guid parentId,
        ImportedItem parent,
        Tally tally
    )
    {
        if (parent.Subtasks is not { Count: > 0 } subs)
            return;

        if (subs.Count is >= 2 and <= 5)
        {
            var ids = subs.Select(_ => Guid.CreateVersion7()).ToList();
            await bus.SendAsync(new BreakDown(parentId, subs.Select(s => s.Title).ToList(), ids));
            tally.Created += subs.Count;

            for (var n = 0; n < subs.Count; n++)
            {
                if (!subs[n].IsDone)
                    continue;
                var on = subs[n].CompletedOn is { } c && c <= today ? c : today;
                await bus.SendAsync(new CompleteItem(ids[n], on));
                tally.Completed++;
            }
            return;
        }

        foreach (var sub in subs)
            await ImportOneAsync(workspaceId, today, sub, $"{parent.Title} › {sub.Title}", tally);
    }

    Task ApplyTagsAsync(Guid workspaceId, Guid itemId, IReadOnlyList<string> names) =>
        uow.RunAsync(async store =>
        {
            var existing = (await store.Tags.ListAsync(workspaceId)).ToDictionary(
                t => t.Name,
                StringComparer.OrdinalIgnoreCase
            );
            var ids = new List<Guid>();
            foreach (
                var name in names
                    .Select(n => n.Trim())
                    .Where(n => n.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            )
            {
                if (!existing.TryGetValue(name, out var tag))
                {
                    tag = new Tag
                    {
                        Id = Guid.CreateVersion7(),
                        WorkspaceId = workspaceId,
                        Name = name,
                    };
                    await store.Tags.UpsertAsync(tag);
                    existing[name] = tag;
                }
                ids.Add(tag.Id);
            }
            await store.Tags.SetItemTagsAsync(itemId, ids);
            return 0;
        });
}
