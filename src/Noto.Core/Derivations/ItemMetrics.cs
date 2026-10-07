using Noto.Core.Models;

namespace Noto.Core.Derivations;

public sealed record ItemMetrics(int Age, int Carry, int Defers)
{
    // Age 0 reads as "New".
    public bool IsNew => Age == 0;
}

public static class MetricsCalculator
{
    public static ItemMetrics Compute(ItemTimeline timeline, DateOnly today)
    {
        var end = timeline.EndDate ?? today;
        var age = Math.Max(0, end.DayNumber - timeline.CreatedOn.DayNumber);
        return new ItemMetrics(age, Carry(timeline, end), Defers(timeline));
    }

    // A day D in (created, end] is carried when the state at its start is planned-open with PlannedFor < D.
    // State is constant between steps, so each segment is counted arithmetically instead of per day.
    static int Carry(ItemTimeline t, DateOnly end)
    {
        var steps = t.Steps;
        var carry = 0;
        for (var i = 0; i < steps.Count; i++)
        {
            var (date, state) = steps[i];
            if (!state.IsPlannedOpen)
                continue;

            var lo = Max(
                date.AddDays(1),
                t.CreatedOn.AddDays(1),
                state.PlannedFor!.Value.AddDays(1)
            );
            var hi = i + 1 < steps.Count ? Min(steps[i + 1].Date, end) : end;
            carry += Math.Max(0, hi.DayNumber - lo.DayNumber + 1);
        }
        return carry;
    }

    // An undone defer ("undo_defer") cancels the defer it reverted.
    static int Defers(ItemTimeline t) =>
        t.PlanEvents.Count(p => p.Kind == "defer")
        - t.PlanEvents.Count(p => p.Kind == "undo_defer");

    static DateOnly Max(params DateOnly[] d) => d.Max();

    static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
