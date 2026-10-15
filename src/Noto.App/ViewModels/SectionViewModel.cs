using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Noto.App.ViewModels;

public sealed partial class SectionViewModel(string title, bool collapsible = true)
    : ObservableObject
{
    public string Title { get; } = title;
    public bool IsCollapsible { get; } = collapsible;
    public ObservableCollection<ItemRowViewModel> Rows { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(VisibleRows))]
    bool _isCollapsed;

    public int? WipLimit { get; init; }

    // Toggling rebuilds the whole flat list, so every header is a new control. This lets only the section
    // that was just toggled animate its arrow, instead of every expanded one turning in sympathy.
    long _toggledAt = long.MinValue;
    public bool JustToggled => Environment.TickCount64 - _toggledAt < 600;

    public void MarkToggled() => _toggledAt = Environment.TickCount64;

    // Calendar cells only: the day this section is, so a drop can plan an item for it. Null on the
    // Unscheduled / Overdue trays, which is why `IsDropTarget` is separate.
    public DateOnly? Day { get; init; }
    public bool IsDropTarget { get; init; }
    public bool IsToday { get; init; }
    public bool IsPast { get; init; }
    public bool IsOutsideMonth { get; init; }
    public bool IsWeekend { get; init; }
    public string DayNumber =>
        Day?.Day.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public string WeekdayLabel =>
        Day?.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture) ?? Title;

    // "3 · 2h": how many open items, and the estimates that are known.
    public string LoadText { get; private set; } = "";
    public bool HasLoad => LoadText.Length > 0;

    // The day's load against what a day holds, for days that have not passed: a thin bar, and in words
    // what is left ("4h free", "1h over"). A day with nothing on it offers to be planned.
    public double MeterPercent { get; private set; }
    public bool HasMeter { get; private set; }
    public bool IsOverMeter { get; private set; }
    public string FreeText { get; private set; } = "";
    public bool OffersPlanDay => IsDropTarget && Day is not null && IsEmpty;

    public void SetMeter(int committed, int capacity, Noto.Core.Models.CapacityUnit unit)
    {
        HasMeter = committed > 0 && capacity > 0;
        IsOverMeter = committed > capacity;
        MeterPercent = capacity == 0 ? 0 : Math.Min(100, 100.0 * committed / capacity);
        FreeText =
            committed == 0 ? $"{Noto.App.Logic.DayAheadText.Amount(capacity, unit)} open"
            : committed > capacity
                ? $"{Noto.App.Logic.DayAheadText.Amount(committed - capacity, unit)} over"
            : $"{Noto.App.Logic.DayAheadText.Amount(capacity - committed, unit)} free";
        OnPropertyChanged(nameof(MeterPercent));
        OnPropertyChanged(nameof(HasMeter));
        OnPropertyChanged(nameof(IsOverMeter));
        OnPropertyChanged(nameof(FreeText));
    }

    // Month cells show a few chips and a "+N more" instead of growing.
    public int MaxShown { get; init; } = int.MaxValue;
    public IEnumerable<ItemRowViewModel> ShownRows => Rows.Take(MaxShown);
    public int HiddenCount => Math.Max(0, Rows.Count - MaxShown);
    public string HiddenText => $"+{HiddenCount} more";
    public bool HasHidden => HiddenCount > 0;

    // Board columns only: the model column this section maps to, so a drop can move an item into it.
    public string? ColumnId { get; init; }
    public bool IsOverWip => WipLimit is { } limit && Rows.Count > limit;
    public string Header =>
        WipLimit is { } limit
            ? $"{Title.ToUpperInvariant()} ({Rows.Count}/{limit})"
            : $"{Title.ToUpperInvariant()} ({Rows.Count})";
    public bool IsEmpty => Rows.Count == 0;

    // A short note on the right of the header (the day planner shows what the section costs).
    public string? HeaderDetail { get; init; }
    public bool HasHeaderDetail => !string.IsNullOrEmpty(HeaderDetail);

    // The day planner's "could go here" section: each row offers a Plan here button.
    public bool OffersPlan { get; init; }
    public IEnumerable<ItemRowViewModel> VisibleRows => IsCollapsed ? [] : Rows;

    public void Replace(IEnumerable<ItemRowViewModel> rows)
    {
        Rows.Clear();
        foreach (var row in rows)
            Rows.Add(row);
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(IsOverWip));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(OffersPlanDay));
        OnPropertyChanged(nameof(VisibleRows));
        OnPropertyChanged(nameof(ShownRows));
        OnPropertyChanged(nameof(HiddenCount));
        OnPropertyChanged(nameof(HiddenText));
        OnPropertyChanged(nameof(HasHidden));
    }

    public void SetLoad(string text)
    {
        LoadText = text;
        OnPropertyChanged(nameof(LoadText));
        OnPropertyChanged(nameof(HasLoad));
    }
}
