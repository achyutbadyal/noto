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
    }
}
