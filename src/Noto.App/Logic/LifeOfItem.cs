using System.Globalization;
using Noto.Core.Derivations;
using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.App.Logic;

public sealed record LifeLine(DateOnly Day, string Text)
{
    public string DayText => Day.ToString("MMM d", CultureInfo.InvariantCulture);
}

// "Life of this item": events and carried days in plain language (docs/07 §10.2).
public static class LifeOfItem
{
    public static IReadOnlyList<LifeLine> Describe(TodoItem item, IReadOnlyList<ItemEvent> events, TimeOnly boundary, DateOnly today)
    {
        var lines = new List<LifeLine>();
        foreach (var e in events.OrderBy(e => e.OccurredAt))
            if (Text(e) is { } text) lines.Add(new(LogicalDate.Of(e.OccurredAt, e.Tz, boundary), text));

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
            if (carried) { run++; continue; }
            if (run > 0) yield return new(day.AddDays(-1), $"carried ×{run}");
            run = 0;
        }
        if (run > 0) yield return new(end, $"carried ×{run}");
    }

    static string? Text(ItemEvent e)
    {
        string? Field(string name) => e.Data?[name]?.GetValue<string>();
        static string Date(string? iso) => iso is null ? "no date" : DateOnly.Parse(iso).ToString("MMM d", CultureInfo.InvariantCulture);

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
            ItemEventType.SomedayChanged => e.Data?["to"]?.GetValue<bool>() == true ? "moved to Someday" : "back from Someday",
            ItemEventType.WaitingStarted => $"waiting on {Field("on")}",
            ItemEventType.WaitingEnded => Field("via") switch { "link" => "unblocked via link", "undo" => null, _ => "no longer waiting" },
            ItemEventType.Completed => "completed",
            ItemEventType.Reopened => "reopened",
            ItemEventType.Dropped => $"dropped ({Humanize(Field("reason"))})",
            ItemEventType.Restored => e.Data?["from"] is null ? "restored" : null,
            ItemEventType.StuckReasonGiven => $"stuck: {Field("reason")?.Replace('_', ' ')}",
            ItemEventType.BrokenDown => $"broken into {e.Data?["child_ids"]?.AsArray().Count} steps",
            ItemEventType.TitleChanged => "renamed",
            ItemEventType.EstimateChanged => "estimate changed",
            ItemEventType.PriorityChanged => "priority changed",
            ItemEventType.DueDateChanged => "due date changed",
            ItemEventType.FocusStopped when (e.Data?["minutes"]?.GetValue<int>() ?? 0) > 0 => $"focused {e.Data!["minutes"]!.GetValue<int>()} min",
            _ => null,
        };
    }

    static string Humanize(string? reason) => reason switch
    {
        "NotNeeded" => "no longer needed",
        "SomeoneElseDidIt" => "someone else did it",
        "NotWorthIt" => "not worth it",
        _ => "other",
    };
}
