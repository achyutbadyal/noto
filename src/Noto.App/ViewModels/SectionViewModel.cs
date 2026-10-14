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
    public IEnumerable<ItemRowViewModel> VisibleRows => IsCollapsed ? [] : Rows;

    public void Replace(IEnumerable<ItemRowViewModel> rows)
    {
        Rows.Clear();
        foreach (var row in rows)
            Rows.Add(row);
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(IsOverWip));
        OnPropertyChanged(nameof(IsEmpty));
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
