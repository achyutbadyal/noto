using Noto.Core.Derivations;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Presets;
using Noto.Core.Time;

namespace Noto.Core.Insights;

public enum SizeBucket
{
    Small,
    Medium,
    Large,
} // ≤30m · 31–119m · ≥2h

public sealed record ItemRecord(
    TodoItem Item,
    ItemTimeline Timeline,
    IReadOnlyList<ItemEvent> Events
);

// An insight is only surfaced once it has enough samples (docs/04 §4.4).
public sealed record Insight<T>(T Data, int Sample);

public sealed record SizeStat(SizeBucket Bucket, int Items, double SameDayRate, double MeanCarry);

public sealed record WeekdayStat(DayOfWeek Day, double MeanPlanned, double MeanDone, int Days);

public sealed record WeekRollover(DateOnly WeekStart, double Rate);

public sealed record AccuracyStat(SizeBucket Bucket, int Items, double MedianRatio); // focused ÷ estimated

public sealed record WaitStat(string Person, int Intervals, double MedianDays);

public sealed record OldOpenItem(TodoItem Item, int Age, int Carry);

public sealed record InsightsReport(
    Insight<IReadOnlyList<SizeStat>>? SizeVsCompletion,
    Insight<IReadOnlyList<WeekdayStat>>? WeekdayLoad,
    Insight<IReadOnlyList<WeekRollover>>? RolloverTrend,
    Insight<IReadOnlyDictionary<string, int>>? StuckMix,
    Insight<IReadOnlyList<AccuracyStat>>? EstimateAccuracy,
    Insight<IReadOnlyList<WaitStat>>? WaitingByPerson,
    Insight<double>? MedianCarryAtCompletion,
    int StaleCount,
    IReadOnlyList<OldOpenItem> OldestOpen
);

public static class InsightsEngine
{
    public const int MinSample = 10;

    public static InsightsReport Compute(
        Workspace ws,
        IReadOnlyList<ItemRecord> records,
        IReadOnlyList<DayStats> dayStats,
        DateOnly today,
        DateOnly from,
        DateOnly to
    )
    {
        var thresholds = PressureThresholds.For(ws.Pressure, ws.PressureOverridesJson);
        var live = records.Where(r => r.Item.DeletedAt is null).ToList();
        var metrics = live.ToDictionary(
            r => r.Item.Id,
            r => MetricsCalculator.Compute(r.Timeline, today)
        );
        var days = dayStats.Where(d => d.Day >= from && d.Day <= to).ToList();

        var open = live.Where(r =>
                r.Timeline.Final
                    is { Status: ItemStatus.Open, IsSomeday: false, IsContainer: false }
            )
            .ToList();
        var stale = open.Count(r =>
            thresholds.StateOf(metrics[r.Item.Id].Carry) == PressureState.Stale
        );
        var oldest = open.Select(r => new OldOpenItem(
                r.Item,
                metrics[r.Item.Id].Age,
                metrics[r.Item.Id].Carry
            ))
            .OrderByDescending(o => o.Age)
            .Take(5)
            .ToList();

        return new InsightsReport(
            Gate(SizeVsCompletion(live, metrics, today, from, to)),
            Gate(WeekdayLoad(days)),
            Gate(RolloverTrend(days)),
            Gate(StuckMix(live, ws.DayBoundary, from, to)),
            Gate(EstimateAccuracy(live, from, to)),
            Gate(WaitingByPerson(live, ws.DayBoundary, today, from, to)),
            Gate(CarryAtCompletion(live, metrics, from, to)),
            stale,
            oldest
        );
    }

    static Insight<T>? Gate<T>(Insight<T> insight) => insight.Sample >= MinSample ? insight : null;

    public static SizeBucket BucketOf(int minutes) =>
        minutes <= 30 ? SizeBucket.Small
        : minutes < 120 ? SizeBucket.Medium
        : SizeBucket.Large;

    static DateOnly Day(ItemEvent e, TimeOnly boundary) =>
        LogicalDate.Of(e.OccurredAt, e.Tz, boundary);

    // The first day the user committed to doing the item.
    static DateOnly? FirstPlanned(ItemRecord r)
    {
        if (r.Timeline.CreatedPlannedFor is { } created)
            return created;
        var plan = r.Events.FirstOrDefault(e =>
            e.Type == ItemEventType.Planned && e.Data?["to"] is not null
        );
        return plan?.Data?["to"]?.GetValue<string>() is { } s ? DateOnly.Parse(s) : null;
    }

    internal static Insight<IReadOnlyList<SizeStat>> SizeVsCompletion(
        List<ItemRecord> records,
        Dictionary<Guid, ItemMetrics> metrics,
        DateOnly today,
        DateOnly from,
        DateOnly to
    )
    {
        var rows = records
            .Where(r =>
                r.Item.EstimateMinutes is > 0
                && FirstPlanned(r) is { } p
                && p >= from
                && p <= to
                && (r.Timeline.Final.Status == ItemStatus.Done || p < today)
            )
            .Select(r => (r, planned: FirstPlanned(r)!.Value))
            .ToList();

        var stats = rows.GroupBy(x => BucketOf(x.r.Item.EstimateMinutes!.Value))
            .OrderBy(g => g.Key)
            .Select(g => new SizeStat(
                g.Key,
                g.Count(),
                g.Count(x =>
                    x.r.Timeline.Final is { Status: ItemStatus.Done } f
                    && f.CompletedOn == x.planned
                ) / (double)g.Count(),
                g.Average(x => metrics[x.r.Item.Id].Carry)
            ))
            .ToList();
        return new(stats, rows.Count);
    }

    static Insight<IReadOnlyList<WeekdayStat>> WeekdayLoad(List<DayStats> days)
    {
        var busy = days.Where(d => Load(d) > 0).ToList();
        var stats = busy.GroupBy(d => d.Day.DayOfWeek)
            .OrderBy(g => ((int)g.Key + 6) % 7)
            .Select(g => new WeekdayStat(g.Key, g.Average(Load), g.Average(d => d.Done), g.Count()))
            .ToList();
        return new(stats, busy.Count);
    }

    static Insight<IReadOnlyList<WeekRollover>> RolloverTrend(List<DayStats> days)
    {
        var busy = days.Where(d => Load(d) > 0).ToList();
        var weeks = busy.GroupBy(d => WeekStart(d.Day))
            .OrderBy(g => g.Key)
            .Select(g => new WeekRollover(g.Key, g.Sum(d => d.CarriedIn) / (double)g.Sum(Load)))
            .ToList();
        return new(weeks, busy.Count);
    }

    internal static Insight<IReadOnlyDictionary<string, int>> StuckMix(
        List<ItemRecord> records,
        TimeOnly boundary,
        DateOnly from,
        DateOnly to
    )
    {
        var reasons = records
            .SelectMany(r => r.Events)
            .Where(e =>
                e.Type == ItemEventType.StuckReasonGiven
                && Day(e, boundary) >= from
                && Day(e, boundary) <= to
            )
            .Select(e => e.Data?["reason"]?.GetValue<string>() ?? "other")
            .GroupBy(r => r)
            .ToDictionary(g => g.Key, g => g.Count());
        return new(reasons, reasons.Values.Sum());
    }

    internal static Insight<IReadOnlyList<AccuracyStat>> EstimateAccuracy(
        List<ItemRecord> records,
        DateOnly from,
        DateOnly to
    )
    {
        var rows = records
            .Where(r =>
                r.Item.EstimateMinutes is > 0
                && r.Timeline.Final is { Status: ItemStatus.Done } f
                && f.CompletedOn >= from
                && f.CompletedOn <= to
            )
            .Select(r =>
                (
                    r.Item,
                    focused: r.Events.Where(e => e.Type == ItemEventType.FocusStopped)
                        .Sum(e => e.Data?["minutes"]?.GetValue<int>() ?? 0)
                )
            )
            .Where(x => x.focused > 0)
            .ToList();

        var stats = rows.GroupBy(x => BucketOf(x.Item.EstimateMinutes!.Value))
            .OrderBy(g => g.Key)
            .Select(g => new AccuracyStat(
                g.Key,
                g.Count(),
                Median(g.Select(x => x.focused / (double)x.Item.EstimateMinutes!.Value))
            ))
            .ToList();
        return new(stats, rows.Count);
    }

    static Insight<IReadOnlyList<WaitStat>> WaitingByPerson(
        List<ItemRecord> records,
        TimeOnly boundary,
        DateOnly today,
        DateOnly from,
        DateOnly to
    )
    {
        var spans = new List<(string Person, int Days)>();
        foreach (var r in records)
        {
            (string Person, DateOnly Start)? current = null;
            foreach (var e in r.Events.OrderBy(e => e.OccurredAt))
            {
                if (e.Type == ItemEventType.WaitingStarted)
                    current = (e.Data?["on"]?.GetValue<string>() ?? "?", Day(e, boundary));
                else if (e.Type == ItemEventType.WaitingEnded && current is { } c)
                {
                    var end = Day(e, boundary);
                    if (c.Start >= from && c.Start <= to)
                        spans.Add((c.Person, end.DayNumber - c.Start.DayNumber));
                    current = null;
                }
            }
            if (current is { } open && open.Start >= from && open.Start <= to)
                spans.Add((open.Person, today.DayNumber - open.Start.DayNumber));
        }

        var stats = spans
            .GroupBy(s => s.Person, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WaitStat(g.Key, g.Count(), Median(g.Select(s => (double)s.Days))))
            .OrderByDescending(w => w.MedianDays)
            .ToList();
        return new(stats, spans.Count);
    }

    static Insight<double> CarryAtCompletion(
        List<ItemRecord> records,
        Dictionary<Guid, ItemMetrics> metrics,
        DateOnly from,
        DateOnly to
    )
    {
        var carries = records
            .Where(r =>
                r.Timeline.Final is { Status: ItemStatus.Done } f
                && f.CompletedOn >= from
                && f.CompletedOn <= to
            )
            .Select(r => (double)metrics[r.Item.Id].Carry)
            .ToList();
        return new(carries.Count == 0 ? 0 : Median(carries), carries.Count);
    }

    static int Load(DayStats d) => d.CarriedIn + d.PlannedIn + d.Added;

    static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    internal static double Median(IEnumerable<double> values)
    {
        var s = values.Order().ToList();
        if (s.Count == 0)
            return 0;
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}

public sealed class InsightsService(IUnitOfWork uow, IClock clock, DerivationService derive)
{
    public async Task<InsightsReport> GetAsync(Guid workspaceId, DateOnly from, DateOnly to)
    {
        var (ws, records, today) = await LoadAsync(workspaceId);
        var stats = await derive.GetDayStatsAsync(workspaceId, from, to);
        return InsightsEngine.Compute(ws, records, stats, today, from, to);
    }

    internal Task<(Workspace Ws, IReadOnlyList<ItemRecord> Records, DateOnly Today)> LoadAsync(
        Guid workspaceId
    ) =>
        uow.RunAsync(async store =>
        {
            var ws =
                await store.Workspaces.GetAsync(workspaceId)
                ?? throw new InvalidOperationException("Workspace not found");
            var items = await store.Items.ListAsync(workspaceId);
            var events = (await store.Events.ListForWorkspaceAsync(workspaceId)).ToLookup(e =>
                e.ItemId
            );
            var records = items
                .Select(i =>
                {
                    var evs = events[i.Id].ToList();
                    return new ItemRecord(i, ItemTimeline.Build(i, evs, ws.DayBoundary), evs);
                })
                .ToList();
            return (ws, (IReadOnlyList<ItemRecord>)records, LogicalDate.Today(ws, clock));
        });
}
