using System.Text.Json.Nodes;
using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.Core.Layouts;

public sealed record BoardColumnDef(string Id, string Name, int? WipLimit = null);

// Stored under the "board" key of workspace.layout_settings.
public sealed record BoardSettings(IReadOnlyList<BoardColumnDef> UserColumns, int DoneDays = 7, int StuckDays = 7)
{
    public static readonly BoardSettings Default = new([]);

    public static BoardSettings FromJson(string? layoutSettingsJson)
    {
        if (string.IsNullOrWhiteSpace(layoutSettingsJson)) return Default;
        if (JsonNode.Parse(layoutSettingsJson)?["board"] is not JsonObject b) return Default;
        var cols = b["columns"]?.AsArray().Select(c => new BoardColumnDef(
            c!["id"]!.GetValue<string>(), c["name"]!.GetValue<string>(), c["wip"]?.GetValue<int>())).ToList() ?? [];
        return new BoardSettings(cols, b["done_days"]?.GetValue<int>() ?? 7, b["stuck_days"]?.GetValue<int>() ?? 7);
    }

    // Merges into the existing layout settings so other layouts keep theirs.
    public string ToJson(string? existing)
    {
        var root = string.IsNullOrWhiteSpace(existing) ? new JsonObject() : JsonNode.Parse(existing)!.AsObject();
        root["board"] = new JsonObject
        {
            ["columns"] = new JsonArray(UserColumns.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.Id, ["name"] = c.Name, ["wip"] = c.WipLimit,
            }).ToArray()),
            ["done_days"] = DoneDays,
            ["stuck_days"] = StuckDays,
        };
        return root.ToJsonString();
    }
}

public sealed record BoardColumn(string Id, string Name, bool IsFixed, int? WipLimit, IReadOnlyList<TodoItem> Items)
{
    public bool IsOverWip => WipLimit is { } limit && Items.Count > limit;
}

public static class BoardIds
{
    public const string Someday = "someday", Backlog = "backlog", Today = "today", Waiting = "waiting", Done = "done";
    public static readonly string[] Fixed = [Someday, Backlog, Today, Waiting, Done];
}

public static class BoardLayout
{
    // Columns: Someday · Backlog · (user columns) · Today · Waiting · Done (N days). Status stays the single
    // source of truth; user columns only hold open, unscheduled items. Future-planned items sit in Backlog.
    public static IReadOnlyList<BoardColumn> Build(IEnumerable<TodoItem> items, DateOnly today, BoardSettings settings)
    {
        var live = items.Where(i => i.DeletedAt is null && !i.IsContainer && i.Status != ItemStatus.Dropped).ToList();
        var userIds = settings.UserColumns.Select(c => c.Id).ToHashSet();

        IReadOnlyList<TodoItem> Rank(IEnumerable<TodoItem> xs) => xs.OrderBy(i => i.ManualRank, StringComparer.Ordinal).ToList();
        bool IsOpen(TodoItem i) => i.Status == ItemStatus.Open;
        bool Unscheduled(TodoItem i) => IsOpen(i) && !i.IsSomeday && i.PlannedFor is null;

        var someday = Rank(live.Where(i => IsOpen(i) && i.IsSomeday));
        var todayItems = Rank(live.Where(i => IsOpen(i) && !i.IsSomeday && i.PlannedFor <= today));
        var waiting = Rank(live.Where(i => i.Status == ItemStatus.Waiting));
        var done = live.Where(i => i.Status == ItemStatus.Done && i.CompletedOn >= today.AddDays(1 - settings.DoneDays))
            .OrderByDescending(i => i.CompletedAt).ToList();
        var backlog = Rank(live.Where(i => IsOpen(i) && !i.IsSomeday && (i.PlannedFor > today ||
            (Unscheduled(i) && (i.BoardColumn is null || !userIds.Contains(i.BoardColumn))))));

        var columns = new List<BoardColumn>
        {
            new(BoardIds.Someday, "Someday", true, null, someday),
            new(BoardIds.Backlog, "Backlog", true, null, backlog),
        };
        columns.AddRange(settings.UserColumns.Select(c => new BoardColumn(c.Id, c.Name, false, c.WipLimit,
            Rank(live.Where(i => Unscheduled(i) && i.BoardColumn == c.Id)))));
        columns.Add(new(BoardIds.Today, "Today", true, null, todayItems));
        columns.Add(new(BoardIds.Waiting, "Waiting", true, null, waiting));
        columns.Add(new(BoardIds.Done, $"Done ({settings.DoneDays} days)", true, null, done));
        return columns;
    }

    // Unscheduled items with no ColumnChanged/Planned event for `days` (the only signal under gentle pressure).
    public static IReadOnlyList<Guid> StuckInColumn(
        IEnumerable<(TodoItem Item, IReadOnlyList<ItemEvent> Events)> items, DateOnly today, TimeOnly boundary, int days)
    {
        var stuck = new List<Guid>();
        foreach (var (item, events) in items)
        {
            if (item.DeletedAt is not null || item.IsContainer || item.IsSomeday || item.Status != ItemStatus.Open || item.PlannedFor is not null) continue;
            var lastMove = events
                .Where(e => e.Type is ItemEventType.ColumnChanged or ItemEventType.Planned or ItemEventType.Created)
                .Select(e => LogicalDate.Of(e.OccurredAt, e.Tz, boundary))
                .DefaultIfEmpty(LogicalDate.Of(item.CreatedAt, item.CreatedTz, boundary)).Max();
            if (today.DayNumber - lastMove.DayNumber >= days) stuck.Add(item.Id);
        }
        return stuck;
    }
}

public sealed record BoardMove(IReadOnlyList<ItemCommand> Commands, bool NeedsWaitingOn);

public static class BoardMoves
{
    // Translates a drag to a column into ordinary commands. Dropping into Waiting needs a person (asked by the UI).
    public static BoardMove Plan(TodoItem item, string targetColumn, DateOnly today, string? waitingOn = null)
    {
        var cmds = new List<ItemCommand>();
        var status = item.Status;
        var planned = item.PlannedFor;
        var someday = item.IsSomeday;

        if (targetColumn == BoardIds.Waiting && string.IsNullOrWhiteSpace(waitingOn) && status != ItemStatus.Waiting)
            return new BoardMove([], true);

        if (status == ItemStatus.Done && targetColumn != BoardIds.Done) { cmds.Add(new ReopenItem(item.Id)); status = ItemStatus.Open; }
        if (status == ItemStatus.Waiting && targetColumn != BoardIds.Waiting) { cmds.Add(new EndWaiting(item.Id)); status = ItemStatus.Open; }

        switch (targetColumn)
        {
            case BoardIds.Done:
                if (item.Status != ItemStatus.Done) cmds.Add(new CompleteItem(item.Id));
                break;
            case BoardIds.Waiting:
                if (item.Status != ItemStatus.Waiting)
                {
                    if (someday) cmds.Add(new SetSomeday(item.Id, false));
                    cmds.Add(new StartWaiting(item.Id, waitingOn!));
                }
                break;
            case BoardIds.Today:
                if (planned != today || someday) cmds.Add(new PlanItem(item.Id, today, PlanKind.Plan));
                break;
            case BoardIds.Someday:
                if (!someday) cmds.Add(new SetSomeday(item.Id, true));
                break;
            default: // backlog or a user column: open and unscheduled, optionally filed under a column
                if (planned is not null || someday) cmds.Add(new PlanItem(item.Id, null, PlanKind.Unschedule));
                var column = targetColumn == BoardIds.Backlog ? null : targetColumn;
                if (item.BoardColumn != column) cmds.Add(new SetBoardColumn(item.Id, column));
                break;
        }
        return new BoardMove(cmds, false);
    }
}
