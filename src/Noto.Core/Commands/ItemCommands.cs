using System.Text.Json.Nodes;
using Noto.Core.Models;

namespace Noto.Core.Commands;

public enum PlanKind { Plan, KeepToday, Defer, Unschedule }

public sealed record CreateItem(
    Guid ItemId, Guid WorkspaceId, string Title,
    DateOnly? PlannedFor = null, bool IsSomeday = false, int? EstimateMinutes = null,
    string Source = "inline") : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(Title)) throw new CommandException("Title is required");
        item.Title = Title.Trim();
        item.IsSomeday = IsSomeday;
        item.PlannedFor = IsSomeday ? null : PlannedFor;
        item.EstimateMinutes = EstimateMinutes;
        return new(ItemEventType.Created, new() { ["planned_for"] = Json.Date(item.PlannedFor), ["is_someday"] = IsSomeday, ["source"] = Source },
                   ItemEventType.Deleted, null);
    }
}

public sealed record PlanItem(Guid ItemId, DateOnly? To, PlanKind Kind) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        Guard.Status(item, ItemStatus.Open, ItemStatus.Waiting);
        var from = item.PlannedFor;
        var to = Kind == PlanKind.KeepToday ? ctx.Today : To;
        if (Kind == PlanKind.Defer && (to is null || to <= ctx.Today)) throw new CommandException("Defer needs a future date");

        item.PlannedFor = to;
        item.IsSomeday = false;
        var kind = KindName(Kind);
        return new(ItemEventType.Planned, Moved(from, to, kind), ItemEventType.Planned, Moved(to, from, "undo_" + kind));
    }

    public static string KindName(PlanKind kind) => kind switch
    {
        PlanKind.Plan => "plan",
        PlanKind.KeepToday => "keep_today",
        PlanKind.Defer => "defer",
        _ => "unschedule",
    };

    static JsonObject Moved(DateOnly? from, DateOnly? to, string kind) =>
        new() { ["from"] = Json.Date(from), ["to"] = Json.Date(to), ["kind"] = kind };
}

public sealed record SetSomeday(Guid ItemId, bool Value) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        Guard.Status(item, ItemStatus.Open);
        item.IsSomeday = Value;
        if (Value) item.PlannedFor = null;
        return new(ItemEventType.SomedayChanged, new() { ["to"] = Value }, ItemEventType.SomedayChanged, new() { ["to"] = !Value });
    }
}

public sealed record StartWaiting(Guid ItemId, string On) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        Guard.Status(item, ItemStatus.Open);
        if (string.IsNullOrWhiteSpace(On)) throw new CommandException("Waiting needs a person or URL");
        item.Status = ItemStatus.Waiting;
        item.WaitingOn = On.Trim();
        return new(ItemEventType.WaitingStarted, new() { ["on"] = item.WaitingOn }, ItemEventType.WaitingEnded, new() { ["via"] = "undo" });
    }
}

public sealed record EndWaiting(Guid ItemId, string Via = "manual") : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        Guard.Status(item, ItemStatus.Waiting);
        var on = item.WaitingOn;
        item.Status = ItemStatus.Open;
        item.WaitingOn = null;
        return new(ItemEventType.WaitingEnded, new() { ["via"] = Via }, ItemEventType.WaitingStarted, new() { ["on"] = on });
    }
}

// `CompletedOn` may be earlier than today ("Already done" in the morning review).
public sealed record CompleteItem(Guid ItemId, DateOnly? CompletedOn = null) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        Guard.Status(item, ItemStatus.Open, ItemStatus.Waiting);
        var day = CompletedOn ?? ctx.Today;
        if (day > ctx.Today) throw new CommandException("Cannot complete in the future");
        item.Status = ItemStatus.Done;
        item.CompletedOn = day;
        item.CompletedAt = ctx.Now;
        item.IsSomeday = false;
        item.WaitingOn = null;
        return new(ItemEventType.Completed, new() { ["completed_on"] = Json.Date(day) }, ItemEventType.Reopened, null);
    }
}

public sealed record ReopenItem(Guid ItemId) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        Guard.Status(item, ItemStatus.Done);
        item.Status = ItemStatus.Open;
        item.CompletedOn = null;
        item.CompletedAt = null;
        return new(ItemEventType.Reopened, null, ItemEventType.Completed, null);
    }
}

public sealed record DropItem(Guid ItemId, DropReason Reason, string? Note = null) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        Guard.Status(item, ItemStatus.Open, ItemStatus.Waiting);
        item.Status = ItemStatus.Dropped;
        item.DroppedAt = ctx.Now;
        item.DropReason = Reason;
        item.IsSomeday = false;
        item.WaitingOn = null;
        return new(ItemEventType.Dropped, new() { ["reason"] = Reason.ToString(), ["note"] = Note }, ItemEventType.Restored, null);
    }
}

public sealed record RestoreItem(Guid ItemId) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        Guard.Status(item, ItemStatus.Dropped);
        item.Status = ItemStatus.Open;
        item.DroppedAt = null;
        item.DropReason = null;
        return new(ItemEventType.Restored, null, ItemEventType.Dropped, null);
    }
}

public sealed record RenameItem(Guid ItemId, string Title) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(Title)) throw new CommandException("Title is required");
        var from = item.Title;
        item.Title = Title.Trim();
        return new(ItemEventType.TitleChanged, new() { ["from"] = from, ["to"] = item.Title },
                   ItemEventType.TitleChanged, new() { ["from"] = item.Title, ["to"] = from });
    }
}

// Splits an item into 2–5 subtasks; the first inherits the parent's planned day (docs/04 §2.2).
public sealed record BreakDown(Guid ItemId, IReadOnlyList<string> Titles, IReadOnlyList<Guid>? ChildIds = null) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        Guard.Status(item, ItemStatus.Open);
        if (item.ParentId is not null || item.IsContainer) throw new CommandException("Only a top-level, unbroken item can be broken down");
        var titles = Titles.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        if (titles.Count is < 2 or > 5) throw new CommandException("Break down into 2–5 subtasks");

        var planned = item.PlannedFor;
        var children = titles.Select((title, n) => new TodoItem
        {
            Id = ChildIds?[n] ?? Guid.CreateVersion7(),
            WorkspaceId = item.WorkspaceId,
            ParentId = item.Id,
            Title = title,
            PlannedFor = n == 0 ? planned : null,
            Priority = item.Priority,
            ManualRank = item.ManualRank,
            CreatedAt = ctx.Now,
            CreatedTz = ctx.Tz,
        }).ToList();

        item.IsContainer = true;
        item.PlannedFor = null;
        item.IsSomeday = false;
        return new(ItemEventType.BrokenDown, new() { ["child_ids"] = new JsonArray(children.Select(c => (JsonNode)c.Id.ToString()).ToArray()) },
                   ItemEventType.BreakDownUndone, new() { ["planned_for"] = Json.Date(planned) })
        { Spawned = children };
    }
}

public sealed record SetEstimate(Guid ItemId, int? Minutes) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        if (Minutes is <= 0) throw new CommandException("Estimate must be positive");
        var from = item.EstimateMinutes;
        item.EstimateMinutes = Minutes;
        return Swap(ItemEventType.EstimateChanged, from, Minutes);
    }

    static Change Swap(ItemEventType type, int? from, int? to) =>
        new(type, new() { ["from"] = from, ["to"] = to }, type, new() { ["from"] = to, ["to"] = from });
}

public sealed record SetPriority(Guid ItemId, int Priority) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        if (Priority is < 0 or > 4) throw new CommandException("Priority is 0–4");
        var from = item.Priority;
        item.Priority = Priority;
        return new(ItemEventType.PriorityChanged, new() { ["from"] = from, ["to"] = Priority },
                   ItemEventType.PriorityChanged, new() { ["from"] = Priority, ["to"] = from });
    }
}

public sealed record SetDueDate(Guid ItemId, DateOnly? Due) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        var from = item.DueDate;
        item.DueDate = Due;
        return new(ItemEventType.DueDateChanged, new() { ["from"] = Json.Date(from), ["to"] = Json.Date(Due) },
                   ItemEventType.DueDateChanged, new() { ["from"] = Json.Date(Due), ["to"] = Json.Date(from) });
    }
}

public sealed record SetNotes(Guid ItemId, string? Notes) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        item.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes;
        return new(ItemEventType.NotesChanged, null, ItemEventType.NotesChanged, null);
    }
}

public sealed record SetTimeOfDay(Guid ItemId, TimeOfDay? Value) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        var from = item.TimeOfDay;
        item.TimeOfDay = Value;
        return new(ItemEventType.TimeOfDayChanged, new() { ["from"] = from?.ToString(), ["to"] = Value?.ToString() },
                   ItemEventType.TimeOfDayChanged, new() { ["from"] = Value?.ToString(), ["to"] = from?.ToString() });
    }
}

public sealed record SetBoardColumn(Guid ItemId, string? Column) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        var from = item.BoardColumn;
        item.BoardColumn = Column;
        return new(ItemEventType.ColumnChanged, new() { ["from"] = from, ["to"] = Column },
                   ItemEventType.ColumnChanged, new() { ["from"] = Column, ["to"] = from });
    }
}

public enum StuckReason { TooBig, Blocked, Unclear, DontWant, NotNeeded }

// Event-only: records why an item keeps slipping; no item state changes.
public sealed record GiveStuckReason(Guid ItemId, StuckReason Reason) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx) =>
        new(ItemEventType.StuckReasonGiven, new() { ["reason"] = Reason switch
        {
            StuckReason.TooBig => "too_big",
            StuckReason.Blocked => "blocked",
            StuckReason.Unclear => "unclear",
            StuckReason.DontWant => "dont_want_to",
            _ => "not_needed",
        } }, ItemEventType.StuckReasonGiven, null);
}

public sealed record DeleteItem(Guid ItemId) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx)
    {
        item.DeletedAt = ctx.Now;
        return new(ItemEventType.Deleted, null, ItemEventType.Restored, new() { ["from"] = "deleted" });
    }
}

static class Guard
{
    public static void Status(TodoItem item, params ItemStatus[] allowed)
    {
        if (!allowed.Contains(item.Status))
            throw new CommandException($"Not allowed while item is {item.Status}");
    }
}

static class Json
{
    public static JsonNode? Date(DateOnly? d) => d?.ToString("yyyy-MM-dd");
}
