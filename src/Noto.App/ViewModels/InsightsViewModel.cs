using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Insights;

namespace Noto.App.ViewModels;

public sealed record InsightRow(string Label, double Fraction, string Value);

public sealed record InsightCard(
    string Headline,
    string? Detail,
    IReadOnlyList<InsightRow> Rows,
    string? Suggestion = null,
    IReadOnlyList<InsightRow>? Sparkline = null
)
{
    public bool HasRows => Rows.Count > 0;
    public bool HasSparkline => Sparkline is { Count: > 0 };
}

// An insight that is still gathering samples. Shown greyed with a progress-to-unlock meter, so the page
// teaches what it will do rather than showing a sentence on an empty page (docs/07 §8.2).
public sealed record LockedInsight(string Title, int Sample, int Required, double Progress)
{
    public string ProgressText => $"{Sample} / {Required}";
}

// Narrative first, charts second: each card is a sentence backed by a small chart (docs/07 §8.2).
public sealed partial class InsightsViewModel(WorkspaceReader reader, Guid workspaceId)
    : ObservableObject
{
    public const int WindowDays = 56;

    public ObservableCollection<InsightCard> Cards { get; } = [];
    public ObservableCollection<LockedInsight> Locked { get; } = [];
    public ObservableCollection<InsightRow> OldestOpen { get; } = [];

    [ObservableProperty]
    string _streakText = "";

    [ObservableProperty]
    string _learningText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOldest), nameof(HasCards), nameof(HasLocked))]
    int _cardCount;

    public bool HasCards => Cards.Count > 0;
    public bool HasLocked => Locked.Count > 0;
    public bool HasOldest => OldestOpen.Count > 0;

    public async Task LoadAsync()
    {
        var snap = await reader.LoadAsync(workspaceId);
        var from = snap.Today.AddDays(-WindowDays);
        var records = await reader.RecordsAsync(workspaceId);
        var stats = await reader.DayStatsAsync(workspaceId, from, snap.Today.AddDays(-1));
        var report = InsightsEngine.Compute(
            snap.Workspace,
            records,
            stats,
            snap.Today,
            from,
            snap.Today
        );

        Cards.Clear();
        Locked.Clear();
        OldestOpen.Clear();
        foreach (var card in Build(report))
            Cards.Add(card);
        foreach (var o in report.OldestOpen)
            OldestOpen.Add(new(o.Item.Title, 0, $"{o.Age}d · carried {o.Carry}×"));

        // What is still learning. The locked ones are the catalogue; nothing here is a placeholder.
        foreach (var s in report.Samples.Where(s => !s.Unlocked))
            Locked.Add(new(s.Title, s.Sample, s.Required, s.Progress));

        var streak = CompletionStreak.Current(stats);
        StreakText =
            streak == 0 ? "" : $"Completion streak: {streak} day{(streak == 1 ? "" : "s")}";
        LearningText =
            Locked.Count == 0 ? ""
            : Locked.Count == 1 ? "One more insight is still gathering history."
            : $"{Locked.Count} more insights unlock as history builds.";
        CardCount = Cards.Count;
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(HasLocked));
        OnPropertyChanged(nameof(HasOldest));
    }

    public static IEnumerable<InsightCard> Build(InsightsReport r)
    {
        if (r.SizeVsCompletion is { } size)
            yield return SizeCard(size.Data);
        if (r.WeekdayLoad is { } week && WeekdayCard(week.Data) is { } weekday)
            yield return weekday;
        if (r.RolloverTrend is { } roll && RolloverCard(roll.Data) is { } rollover)
            yield return rollover;
        if (r.StuckMix is { } stuck && stuck.Data.Count > 0)
            yield return StuckCard(stuck.Data);
        if (r.EstimateAccuracy is { } acc && acc.Data.Count > 0)
            yield return AccuracyCard(acc.Data);
        if (r.WaitingByPerson is { } wait && wait.Data.Count > 0)
            yield return WaitCard(wait.Data);
    }

    static string Bucket(SizeBucket b) =>
        b switch
        {
            SizeBucket.Small => "Items ≤30m",
            SizeBucket.Medium => "Items 30m–2h",
            _ => "Items ≥2h",
        };

    static string Pct(double v) => $"{Math.Round(v * 100)}%";

    static InsightCard SizeCard(IReadOnlyList<SizeStat> stats)
    {
        var small = stats.FirstOrDefault(s => s.Bucket == SizeBucket.Small);
        var large = stats.FirstOrDefault(s => s.Bucket == SizeBucket.Large);
        var skewed =
            small is not null && large is not null && small.SameDayRate - large.SameDayRate >= 0.2;
        return new InsightCard(
            skewed
                ? "You finish small things fast and big things rarely."
                : "Item size doesn't change much whether you finish on time.",
            null,
            stats
                .Select(s => new InsightRow(
                    Bucket(s.Bucket),
                    s.SameDayRate,
                    $"done same day {Pct(s.SameDayRate)}"
                ))
                .ToList(),
            large is { SameDayRate: < 0.5 }
                ? "Try breaking ≥2h items down during the morning review."
                : null
        );
    }

    static InsightCard? WeekdayCard(IReadOnlyList<WeekdayStat> stats)
    {
        if (stats.Count == 0)
            return null;
        var worst = stats.OrderByDescending(s => s.MeanPlanned - s.MeanDone).First();
        var max = stats.Max(s => s.MeanPlanned);
        var gap = worst.MeanPlanned - worst.MeanDone;
        var headline =
            gap >= 1 ? $"{worst.Day}s are overloaded." : "Your weekdays are evenly loaded.";
        var detail =
            gap >= 1
                ? $"You plan {worst.MeanPlanned:0.#} items on {worst.Day}s and finish {worst.MeanDone:0.#}."
                : null;
        return new InsightCard(
            headline,
            detail,
            stats
                .Select(s => new InsightRow(
                    s.Day.ToString()[..3],
                    max == 0 ? 0 : s.MeanPlanned / max,
                    $"{s.MeanPlanned:0.#} planned · {s.MeanDone:0.#} done"
                ))
                .ToList()
        );
    }

    static InsightCard? RolloverCard(IReadOnlyList<WeekRollover> weeks)
    {
        if (weeks.Count == 0)
            return null;
        var max = Math.Max(0.01, weeks.Max(w => w.Rate));
        // A trend reads as a sparkline, not as a list of bars with labels (docs/07 §8.2).
        var spark = weeks
            .Select(w => new InsightRow(
                w.WeekStart.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture),
                w.Rate / max,
                Pct(w.Rate)
            ))
            .ToList();
        if (weeks.Count < 2)
            return new InsightCard(
                $"Rollover this week: {Pct(weeks[0].Rate)}.",
                null,
                [],
                null,
                spark
            );

        var (first, last) = (weeks[0].Rate, weeks[^1].Rate);
        var verb =
            last < first - 0.03 ? "down"
            : last > first + 0.03 ? "up"
            : "steady";
        var headline =
            verb == "steady"
                ? $"Rollover rate is steady at {Pct(last)}."
                : $"Rollover rate is {verb}: {Pct(first)} → {Pct(last)} over {weeks.Count} weeks.";
        var detail =
            verb == "down" ? "Lower is better — this is the loop working."
            : verb == "up" ? "Higher means more is being carried than planned."
            : $"Across {weeks.Count} weeks.";
        return new InsightCard(headline, detail, [], null, spark);
    }

    static InsightCard StuckCard(IReadOnlyDictionary<string, int> mix)
    {
        var total = mix.Values.Sum();
        var top = mix.OrderByDescending(kv => kv.Value).First();
        return new InsightCard(
            $"Most of your stuck items were “{top.Key.Replace('_', ' ')}”.",
            null,
            mix.OrderByDescending(kv => kv.Value)
                .Select(kv => new InsightRow(
                    kv.Key.Replace('_', ' '),
                    (double)kv.Value / total,
                    kv.Value.ToString()
                ))
                .ToList(),
            top.Key == "too_big" ? "Break big items down as soon as they're carried once." : null
        );
    }

    static InsightCard AccuracyCard(IReadOnlyList<AccuracyStat> stats)
    {
        var overall = stats.OrderBy(s => s.MedianRatio).ElementAt(stats.Count / 2).MedianRatio;
        var headline =
            overall > 1.15 ? "Things take longer than you estimate."
            : overall < 0.85 ? "You finish faster than you estimate."
            : "Your estimates are about right.";
        var max = Math.Max(1.0, stats.Max(s => s.MedianRatio));
        return new InsightCard(
            headline,
            null,
            stats
                .Select(s => new InsightRow(
                    Bucket(s.Bucket),
                    s.MedianRatio / max,
                    $"takes {s.MedianRatio:0.0}× the estimate"
                ))
                .ToList()
        );
    }

    static InsightCard WaitCard(IReadOnlyList<WaitStat> stats)
    {
        var slowest = stats.OrderByDescending(s => s.MedianDays).First();
        var max = stats.Max(s => s.MedianDays);
        return new InsightCard(
            $"Waiting on {slowest.Person} takes {slowest.MedianDays:0.#} days at the median.",
            null,
            stats
                .OrderByDescending(s => s.MedianDays)
                .Take(5)
                .Select(s => new InsightRow(
                    s.Person,
                    max == 0 ? 0 : s.MedianDays / max,
                    $"{s.MedianDays:0.#}d · {s.Intervals}×"
                ))
                .ToList()
        );
    }
}
