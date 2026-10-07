using Noto.Core.Commands;
using Noto.Core.Derivations;
using Noto.Core.Insights;
using Noto.Core.Models;
using Noto.Core.Recurrence;
using Noto.Core.Time;
using Noto.Core.Workspaces;

namespace Noto.Core.Layouts;

public sealed record Win(TodoItem Item, int Age, int Carry);
public sealed record SomedayEntry(TodoItem Item, int DaysUntouched, bool PreselectDrop);
public sealed record StuckPatterns(IReadOnlyDictionary<string, int> Mix, string? TopReason, StuckFix? Suggestion);

public sealed record WeeklyReviewData(
    DateOnly WeekStart,
    IReadOnlyList<Win> Wins,
    Win? Finally,
    IReadOnlyList<SomedayEntry> SomedaySweep,
    StuckPatterns Stuck,
    IReadOnlyList<SizeStat> SizeCarry,
    IReadOnlyList<AccuracyStat> EstimateCheck,
    IReadOnlyList<string> NextWeekOutcomes);

public static class WeeklyReviewBuilder
{
    public const int UntouchedDropDays = 60;
    const int PatternWindowDays = 28;

    public static WeeklyReviewData Build(
        Workspace ws, IReadOnlyList<ItemRecord> records, DateOnly today, DateOnly weekStart, IReadOnlyList<string> nextWeekOutcomes)
    {
        var weekEnd = weekStart.AddDays(6);
        var live = records.Where(r => r.Item.DeletedAt is null).ToList();
        var metrics = live.ToDictionary(r => r.Item.Id, r => MetricsCalculator.Compute(r.Timeline, today));

        var wins = live
            .Where(r => r.Timeline.Final is { Status: ItemStatus.Done } f && f.CompletedOn >= weekStart && f.CompletedOn <= weekEnd)
            .Select(r => new Win(r.Item, metrics[r.Item.Id].Age, metrics[r.Item.Id].Carry))
            .OrderByDescending(w => w.Age).ToList();

        var sweep = live
            .Where(r => r.Timeline.Final is { Status: ItemStatus.Open, IsSomeday: true })
            .Select(r =>
            {
                var touched = r.Events.Select(e => LogicalDate.Of(e.OccurredAt, e.Tz, ws.DayBoundary))
                    .DefaultIfEmpty(r.Timeline.CreatedOn).Max();
                var days = today.DayNumber - touched.DayNumber;
                return new SomedayEntry(r.Item, days, days >= UntouchedDropDays);
            })
            .OrderByDescending(s => s.DaysUntouched).ToList();

        var from = weekEnd.AddDays(1 - PatternWindowDays);
        var mix = InsightsEngine.StuckMix(live, ws.DayBoundary, from, weekEnd).Data;
        var top = mix.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
        var suggestion = top is null ? null : StuckPrompt.FixFor(ParseReason(top));

        return new WeeklyReviewData(
            weekStart, wins, wins.FirstOrDefault(), sweep,
            new StuckPatterns(mix, top, suggestion),
            InsightsEngine.SizeVsCompletion(live, metrics, today, from, weekEnd).Data,
            InsightsEngine.EstimateAccuracy(live, weekStart, weekEnd).Data,
            nextWeekOutcomes);
    }

    static StuckReason ParseReason(string wire) => wire switch
    {
        "too_big" => StuckReason.TooBig,
        "blocked" => StuckReason.Blocked,
        "unclear" => StuckReason.Unclear,
        "dont_want_to" => StuckReason.DontWant,
        _ => StuckReason.NotNeeded,
    };
}

public sealed class WeeklyReviewService(InsightsService insights, DayNoteService notes)
{
    public async Task<WeeklyReviewData> GetAsync(Guid workspaceId, DateOnly anyDayInWeek)
    {
        var weekStart = RRule.MondayOf(anyDayInWeek);
        var (ws, records, today) = await insights.LoadAsync(workspaceId);
        var outcomes = await notes.GetOutcomesAsync(workspaceId, weekStart.AddDays(7));
        return WeeklyReviewBuilder.Build(ws, records, today, weekStart, outcomes);
    }
}
