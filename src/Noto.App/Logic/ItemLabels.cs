using Noto.Core.Derivations;
using Noto.Core.Models;
using Noto.Core.Text;

namespace Noto.App.Logic;

public static class ItemLabels
{
    // "Deploy v2.3, planned, carried 4 times, estimate 1 hour" — read by screen readers on every row.
    public static string Describe(
        TodoItem item,
        ItemMetrics metrics,
        DateOnly today,
        bool isNow,
        bool stuck,
        int? fallbackMinutes = null
    )
    {
        var parts = new List<string> { item.Title, StateWord(item, today, isNow) };

        if (metrics.Carry > 0)
            parts.Add(metrics.Carry == 1 ? "carried 1 time" : $"carried {metrics.Carry} times");
        if (stuck)
            parts.Add("stuck");
        if (item.EstimateMinutes is { } est)
            parts.Add($"estimate {Duration.Spoken(est)}");
        else if (fallbackMinutes is { } guess)
            parts.Add($"estimate about {Duration.Spoken(guess)}");
        if (item.Priority > 0)
            parts.Add($"priority {PriorityWord(item.Priority)}");
        if (item.DueDate is { } due)
            parts.Add(
                $"due {due.ToString("MMMM d", System.Globalization.CultureInfo.InvariantCulture)}"
            );
        return string.Join(", ", parts);
    }

    public static string StateWord(TodoItem item, DateOnly today, bool isNow) =>
        item.Status switch
        {
            ItemStatus.Done => "done",
            ItemStatus.Dropped => "dropped",
            ItemStatus.Waiting => $"waiting on {item.WaitingOn}",
            _ when isNow => "now",
            _ when item.IsContainer => "broken down",
            _ when item.IsSomeday => "someday",
            _ when item.PlannedFor is null => "unscheduled",
            _ when item.PlannedFor < today => "needs a decision",
            _ when item.PlannedFor > today => "scheduled",
            _ => "planned",
        };

    public static string PriorityWord(int priority) =>
        priority switch
        {
            1 => "low",
            2 => "medium",
            3 => "high",
            _ => "critical",
        };
}
