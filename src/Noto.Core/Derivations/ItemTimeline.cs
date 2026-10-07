using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.Core.Derivations;

public readonly record struct ItemState(
    ItemStatus Status,
    bool IsSomeday,
    bool IsContainer,
    DateOnly? PlannedFor,
    DateOnly? CompletedOn,
    DateOnly? DroppedOn
)
{
    public static readonly ItemState Initial = new(ItemStatus.Open, false, false, null, null, null);

    // The state that makes a day count as "carried" (docs/04 §4.1).
    public bool IsPlannedOpen =>
        Status == ItemStatus.Open && !IsSomeday && !IsContainer && PlannedFor is not null;
}

// Replays an item's events into per-logical-day states. Everything in 04 §4 is derived from this.
public sealed class ItemTimeline
{
    readonly List<(DateOnly Date, ItemState State)> _steps;

    ItemTimeline(
        DateOnly createdOn,
        DateOnly? createdPlannedFor,
        IReadOnlyList<(DateOnly, string)> planEvents,
        List<(DateOnly, ItemState)> steps
    )
    {
        CreatedOn = createdOn;
        CreatedPlannedFor = createdPlannedFor;
        PlanEvents = planEvents;
        _steps = steps;
    }

    public DateOnly CreatedOn { get; }
    public DateOnly? CreatedPlannedFor { get; }
    public IReadOnlyList<(DateOnly Date, string Kind)> PlanEvents { get; }
    public ItemState Final => _steps.Count == 0 ? ItemState.Initial : _steps[^1].State;
    internal IReadOnlyList<(DateOnly Date, ItemState State)> Steps => _steps;

    public static ItemTimeline Build(
        TodoItem item,
        IReadOnlyList<ItemEvent> events,
        TimeOnly boundary
    )
    {
        DateOnly? createdPlanned = null;
        var plans = new List<(DateOnly, string)>();
        var steps = new List<(DateOnly Date, ItemState State)>();

        var state = ItemState.Initial;
        foreach (var e in events.OrderBy(e => e.OccurredAt))
        {
            var day = LogicalDate.Of(e.OccurredAt, e.Tz, boundary);
            state = Apply(state, e, day);

            if (e.Type == ItemEventType.Created)
                createdPlanned = Date(e, "planned_for");
            if (e.Type == ItemEventType.Planned)
                plans.Add((day, e.Data?["kind"]?.GetValue<string>() ?? ""));

            // Events sharing a logical day collapse into one step: only the end-of-day state matters.
            if (steps.Count > 0 && steps[^1].Date == day)
                steps[^1] = (day, state);
            else
                steps.Add((day, state));
        }

        return new ItemTimeline(
            LogicalDate.Of(item.CreatedAt, item.CreatedTz, boundary),
            createdPlanned,
            plans,
            steps
        );
    }

    // State after every event whose logical date is < `day`.
    public ItemState StateAtStartOf(DateOnly day)
    {
        var state = ItemState.Initial;
        foreach (var (date, s) in _steps)
        {
            if (date >= day)
                break;
            state = s;
        }
        return state;
    }

    // Logical date the item stopped aging, or null while it is still live.
    public DateOnly? EndDate =>
        Final.Status switch
        {
            ItemStatus.Done => Final.CompletedOn,
            ItemStatus.Dropped => Final.DroppedOn,
            _ => null,
        };

    static ItemState Apply(ItemState s, ItemEvent e, DateOnly day) =>
        e.Type switch
        {
            ItemEventType.Created => s with
            {
                Status = ItemStatus.Open,
                IsSomeday = e.Data?["is_someday"]?.GetValue<bool>() ?? false,
                PlannedFor = Date(e, "planned_for"),
            },
            ItemEventType.Planned => s with { PlannedFor = Date(e, "to"), IsSomeday = false },
            ItemEventType.SomedayChanged => e.Data?["to"]?.GetValue<bool>() == true
                ? s with
                {
                    IsSomeday = true,
                    PlannedFor = null,
                }
                : s with
                {
                    IsSomeday = false,
                },
            ItemEventType.WaitingStarted => s with { Status = ItemStatus.Waiting },
            ItemEventType.WaitingEnded => s with { Status = ItemStatus.Open },
            ItemEventType.Completed => s with
            {
                Status = ItemStatus.Done,
                CompletedOn = Date(e, "completed_on") ?? day,
                IsSomeday = false,
            },
            ItemEventType.Reopened => s with { Status = ItemStatus.Open, CompletedOn = null },
            ItemEventType.Dropped => s with
            {
                Status = ItemStatus.Dropped,
                DroppedOn = day,
                IsSomeday = false,
            },
            ItemEventType.Restored when e.Data?["from"]?.GetValue<string>() == "deleted" => s, // undelete: no status change
            ItemEventType.Restored => s with { Status = ItemStatus.Open, DroppedOn = null },
            ItemEventType.BrokenDown => s with { IsContainer = true, PlannedFor = null },
            ItemEventType.BreakDownUndone => s with
            {
                IsContainer = false,
                PlannedFor = Date(e, "planned_for"),
            },
            _ => s,
        };

    static DateOnly? Date(ItemEvent e, string field) =>
        e.Data?[field]?.GetValue<string>() is { } v ? DateOnly.ParseExact(v, "yyyy-MM-dd") : null;
}
