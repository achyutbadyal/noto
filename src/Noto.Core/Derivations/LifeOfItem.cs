using System.Globalization;
using System.Text.Json;
using Noto.Core.Derivations;
using Noto.Core.Models;
using Noto.Core.Text;
using Noto.Core.Time;

namespace Noto.Core.Derivations;

public sealed record LifeLine(DateOnly Day, string Text)
{
    public string DayText => Day.ToString("MMM d", CultureInfo.InvariantCulture);
}

// "Life of this item": events and carried days in plain language (docs/07 §10.2).
public static class LifeOfItem
{
    public static IReadOnlyList<LifeLine> Describe(
        TodoItem item,
        IReadOnlyList<ItemEvent> events,
        TimeOnly boundary,
        DateOnly today
    )
    {
        var lines = new List<LifeLine>();
        foreach (var e in events.OrderBy(e => e.OccurredAt))
            if (Text(e) is { } text)
                lines.Add(new(LogicalDate.Of(e.OccurredAt, e.Tz, boundary), text));

        lines.AddRange(CarryRuns(ItemTimeline.Build(item, events, boundary), today));
        return lines.OrderBy(l => l.Day).ToList(); // stable: events stay ahead of carry lines on the same day
    }

    // Consecutive carried days collapse into one line dated at the end of the run: "carried ×3".
    static IEnumerable<LifeLine> CarryRuns(ItemTimeline timeline, DateOnly today)
    {
        var end = timeline.EndDate ?? today;
        var run = 0;
        for (var day = timeline.CreatedOn.AddDays(1); day <= end; day = day.AddDays(1))
        {
            var state = timeline.StateAtStartOf(day);
            var carried = state.IsPlannedOpen && state.PlannedFor < day;
            if (carried)
            {
                run++;
                continue;
            }
            if (run > 0)
                yield return new(day.AddDays(-1), $"carried ×{run}");
            run = 0;
        }
        if (run > 0)
            yield return new(end, $"carried ×{run}");
    }

    static string? Text(ItemEvent e)
    {
        string? Field(string name) => e.Data?[name]?.GetValue<string>();

        int? Number(string name)
        {
            var node = e.Data?[name];
            if (node is null || node.GetValueKind() == JsonValueKind.Null)
                return null;
            return node.GetValue<int>();
        }

        static string Date(string? iso) =>
            iso is null
                ? "no date"
                : DateOnly.Parse(iso).ToString("MMM d", CultureInfo.InvariantCulture);

        // "estimate 30m → 1h", "priority none → P2" — the value, not just that something changed.
        static string Minutes(int? value) => value is null ? "none" : Duration.Short(value.Value);
        static string Priority(int? value) => value is null or 0 ? "none" : $"P{value}";
        static string Slot(string? value) => value is null ? "anytime" : value.ToLowerInvariant();

        return e.Type switch
        {
            ItemEventType.Created => "created",
            ItemEventType.Planned => Field("kind") switch
            {
                "defer" => $"deferred to {Date(Field("to"))}",
                "keep_today" => "kept for today",
                "unschedule" => "unscheduled",
                "plan" => $"planned for {Date(Field("to"))}",
                _ => null, // undo bookkeeping stays out of the story
            },
            ItemEventType.SomedayChanged => e.Data?["to"]?.GetValue<bool>() == true
                ? "moved to Someday"
                : "back from Someday",
            ItemEventType.WaitingStarted => $"waiting on {Field("on")}",
            ItemEventType.WaitingEnded => Field("via") switch
            {
                "link" => "unblocked via link",
                "undo" => null,
                _ => "no longer waiting",
            },
            ItemEventType.Completed => "completed",
            ItemEventType.Reopened => "reopened",
            ItemEventType.Dropped => $"dropped ({Humanize(Field("reason"))})",
            ItemEventType.Deleted => "deleted",
            ItemEventType.Restored => "restored",
            ItemEventType.StuckReasonGiven => $"stuck: {Field("reason")?.Replace('_', ' ')}",
            ItemEventType.BrokenDown =>
                $"broken into {e.Data?["child_ids"]?.AsArray().Count} steps",
            ItemEventType.TitleChanged => "renamed",
            ItemEventType.EstimateChanged =>
                $"estimate {Minutes(Number("from"))} → {Minutes(Number("to"))}",
            ItemEventType.PriorityChanged =>
                $"priority {Priority(Number("from"))} → {Priority(Number("to"))}",
            ItemEventType.DueDateChanged => $"due {Date(Field("from"))} → {Date(Field("to"))}",
            ItemEventType.TimeOfDayChanged =>
                $"day part {Slot(Field("from"))} → {Slot(Field("to"))}",
            ItemEventType.ColumnChanged => Field("to") is { } column
                ? $"moved to {column}"
                : "moved out of a column",
            ItemEventType.FocusStopped when (e.Data?["minutes"]?.GetValue<int>() ?? 0) > 0 =>
                $"focused {e.Data!["minutes"]!.GetValue<int>()} min",
            _ => null,
        };
    }

    static string Humanize(string? reason) =>
        reason switch
        {
            "NotNeeded" => "no longer needed",
            "SomeoneElseDidIt" => "someone else did it",
            "NotWorthIt" => "not worth it",
            _ => "other",
        };
}
