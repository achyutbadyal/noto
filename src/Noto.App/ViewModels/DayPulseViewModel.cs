using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Noto.Core.Derivations;
using Noto.Core.Models;
using Noto.Core.Text;

namespace Noto.App.ViewModels;

public enum PulseKind
{
    Past,
    Today,
    Future,
}

// One line of the hover card: a count and what it counts. `Key` picks the colour of its dot.
public sealed record PulseRow(string Label, string Value, string Key)
{
    public bool IsDone => Key == "done";
    public bool IsLeft => Key == "left";
    public bool IsDefer => Key == "defer";
    public bool IsDrop => Key == "drop";
}

// One day of the pulse: a capsule whose outline is what was planned and whose fill is what got done.
// The chip is updated in place on every reload, so completing a task makes today's fill rise rather
// than the whole strip being rebuilt.
public sealed partial class DayPulseChipViewModel : ObservableObject
{
    [ObservableProperty]
    DateOnly _day;

    [ObservableProperty]
    PulseKind _kind;

    [ObservableProperty]
    double _trackHeight;

    [ObservableProperty]
    double _fillHeight;

    [ObservableProperty]
    bool _isComplete;

    [ObservableProperty]
    bool _isEmpty;

    [ObservableProperty]
    string _letter = "";

    [ObservableProperty]
    string _title = "";

    [ObservableProperty]
    string _headline = "";

    [ObservableProperty]
    string _hint = "";

    [ObservableProperty]
    IReadOnlyList<PulseRow> _rows = [];

    public bool HasHint => Hint.Length > 0;
    public bool IsToday => Kind == PulseKind.Today;
    public bool IsPast => Kind == PulseKind.Past;
    public bool IsFuture => Kind == PulseKind.Future;
    public string AutomationName => $"{Title}: {Headline}";

    partial void OnKindChanged(PulseKind value)
    {
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(IsPast));
        OnPropertyChanged(nameof(IsFuture));
    }

    partial void OnHintChanged(string value) => OnPropertyChanged(nameof(HasHint));

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(AutomationName));

    partial void OnHeadlineChanged(string value) => OnPropertyChanged(nameof(AutomationName));
}

// The Day view's rhythm strip: the last few days, today and the next couple, as planned-versus-done
// capsules. Past days come from the day-stats derivation (cached, so a reload stays cheap); today is read
// from the live snapshot; days ahead are only what is scheduled so far, since nothing has happened yet.
public sealed class DayPulseViewModel
{
    public const int Behind = 5;
    public const int Ahead = 2;
    const double BarHeight = 34;
    const double MinTrack = 5;

    public ObservableCollection<DayPulseChipViewModel> Chips { get; } = [];

    sealed record Raw(
        DateOnly Day,
        PulseKind Kind,
        int Planned,
        int Done,
        int Left,
        int Deferred,
        int Dropped,
        int Minutes
    );

    public void Load(
        DateOnly today,
        IReadOnlyList<DayStats> past,
        IReadOnlyList<TodoItem> items,
        int plannedToday,
        int doneToday,
        int leftToday,
        int fallbackMinutes
    )
    {
        var byDay = past.ToDictionary(d => d.Day);
        var raws = new List<Raw>();
        for (var offset = Behind; offset >= 1; offset--)
        {
            var day = today.AddDays(-offset);
            raws.Add(
                byDay.TryGetValue(day, out var s)
                    ? new Raw(
                        day,
                        PulseKind.Past,
                        s.CarriedIn + s.PlannedIn + s.Added,
                        s.Done,
                        s.OpenAtEnd,
                        s.DeferredOut,
                        s.Dropped,
                        s.PlannedMinutes
                    )
                    : new Raw(day, PulseKind.Past, 0, 0, 0, 0, 0, 0)
            );
        }

        int Minutes(IEnumerable<TodoItem> set) =>
            set.Sum(i => i.EstimateMinutes ?? fallbackMinutes);
        var openToday = items.Where(i =>
            i.Status == ItemStatus.Open && !i.IsContainer && i.PlannedFor <= today
        );
        raws.Add(
            new Raw(
                today,
                PulseKind.Today,
                plannedToday,
                doneToday,
                leftToday,
                0,
                0,
                Minutes(openToday)
            )
        );

        for (var ahead = 1; ahead <= Ahead; ahead++)
        {
            var day = today.AddDays(ahead);
            var planned = items
                .Where(i => i.Status == ItemStatus.Open && !i.IsContainer && i.PlannedFor == day)
                .ToList();
            raws.Add(
                new Raw(
                    day,
                    PulseKind.Future,
                    planned.Count,
                    0,
                    planned.Count,
                    0,
                    0,
                    Minutes(planned)
                )
            );
        }

        // One scale for the whole strip, so a heavy day stands taller than a light one.
        var peak = Math.Max(1, raws.Max(r => Math.Max(r.Planned, r.Done)));
        var unit = (BarHeight - MinTrack) / peak;

        while (Chips.Count < raws.Count)
            Chips.Add(new DayPulseChipViewModel());
        for (var i = 0; i < raws.Count; i++)
            Fill(Chips[i], raws[i], today, unit);
    }

    static void Fill(DayPulseChipViewModel chip, Raw r, DateOnly today, double unit)
    {
        var size = Math.Max(r.Planned, r.Done);
        chip.Day = r.Day;
        chip.Kind = r.Kind;
        chip.IsEmpty = size == 0;
        chip.TrackHeight = size == 0 ? MinTrack : MinTrack + size * unit;
        chip.FillHeight = r.Done == 0 ? 0 : MinTrack + r.Done * unit;
        chip.IsComplete = r.Kind != PulseKind.Future && r.Planned > 0 && r.Done >= r.Planned;
        chip.Letter = r.Day.ToString("ddd", CultureInfo.InvariantCulture)[..1];
        chip.Title = TitleFor(r.Day, today);
        (chip.Headline, chip.Hint, chip.Rows) = Describe(r);
    }

    static string TitleFor(DateOnly day, DateOnly today) =>
        (day.DayNumber - today.DayNumber) switch
        {
            0 => "Today",
            -1 => "Yesterday",
            1 => "Tomorrow",
            _ => day.ToString("dddd, MMM d", CultureInfo.InvariantCulture),
        };

    static (string Headline, string Hint, IReadOnlyList<PulseRow> Rows) Describe(Raw r)
    {
        var time = r.Minutes > 0 ? $"~{Duration.Short(r.Minutes)}" : "";
        if (r.Kind == PulseKind.Future)
        {
            return r.Planned == 0
                ? ("Nothing planned yet", "Click to open", [])
                : (
                    $"{r.Planned} planned",
                    "Click to open",
                    time.Length == 0 ? [] : [new("Estimated", time, "plan")]
                );
        }

        if (r.Planned == 0 && r.Done == 0)
            return (
                r.Kind == PulseKind.Today ? "Nothing planned yet" : "Nothing was planned",
                r.Kind == PulseKind.Today ? "" : "Click to open the log",
                []
            );

        var rows = new List<PulseRow> { new("Planned", r.Planned.ToString(), "plan") };
        rows.Add(new("Done", r.Done.ToString(), "done"));
        rows.Add(
            new(r.Kind == PulseKind.Today ? "Still open" : "Left over", r.Left.ToString(), "left")
        );
        if (r.Deferred > 0)
            rows.Add(new("Deferred", r.Deferred.ToString(), "defer"));
        if (r.Dropped > 0)
            rows.Add(new("Dropped", r.Dropped.ToString(), "drop"));
        if (time.Length > 0)
            rows.Add(new("Time planned", time, "plan"));

        var headline = r.Planned == 0 ? $"{r.Done} done" : $"{r.Done} of {r.Planned} done";
        return (headline, r.Kind == PulseKind.Today ? "" : "Click to open the log", rows);
    }
}
