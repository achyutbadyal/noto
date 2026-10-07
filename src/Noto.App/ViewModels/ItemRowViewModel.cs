using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Derivations;
using Noto.Core.Models;
using Noto.Core.Presets;

namespace Noto.App.ViewModels;

public sealed partial class ItemRowViewModel : ObservableObject
{
    public required TodoItem Item { get; init; }
    public required ItemMetrics Metrics { get; init; }
    // Semantic icon name (resolved to a monochrome vector by IconConverters); never an emoji.
    public required string Glyph { get; init; }
    public required bool IsNow { get; init; }
    public required string CarryText { get; init; }
    public required PressureState Pressure { get; init; }
    public required bool ShowBar { get; init; }
    public required bool IsStuck { get; init; }
    public required string EstimateText { get; init; }
    public required string PriorityText { get; init; }
    public required string AutomationName { get; init; }
    public string? Breadcrumb { get; init; }
    // Set on Today (all): the 3px workspace colour bar.
    public string? WorkspaceAccent { get; set; }
    public bool HasWorkspaceAccent => WorkspaceAccent is not null;
    public string? ProgressText { get; init; }

    public Guid Id => Item.Id;
    public string Title => Item.Title;
    public bool HasCarry => CarryText.Length > 0;
    public bool HasEstimate => EstimateText.Length > 0;
    public bool HasPriority => PriorityText.Length > 0;
    public bool HasBreadcrumb => Breadcrumb is not null;
    public bool IsWarm => Pressure == PressureState.Warm;
    public bool IsHot => Pressure == PressureState.Hot;
    public bool IsStale => Pressure == PressureState.Stale;
    public bool BarHot => ShowBar && Pressure == PressureState.Hot;
    public bool BarStale => ShowBar && Pressure == PressureState.Stale;
    public bool IsDone => Item.Status == ItemStatus.Done;
    public bool IsWaiting => Item.Status == ItemStatus.Waiting;
    public bool IsDropped => Item.Status == ItemStatus.Dropped;

    [ObservableProperty] bool _isFocused;
    [ObservableProperty] bool _isSelected;
    [ObservableProperty] bool _isEditing;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasExtra))] string? _extraText;
    public bool HasExtra => ExtraText is not null;

    // Set by the owning list so row-level clicks reach it.
    public ItemListViewModel? Owner { get; set; }

    [RelayCommand]
    void Focus() => Owner?.SetFocus(this);

    [RelayCommand]
    async Task ToggleCompleteAsync()
    {
        if (Owner is { } owner) await owner.ToggleCompleteCommand.ExecuteAsync(this);
    }

    [RelayCommand]
    void Edit()
    {
        if (Owner is not { } owner) return;
        owner.SetFocus(this);
        owner.BeginEdit();
    }
}

public static class ItemRowFactory
{
    public static ItemRowViewModel Create(
        TodoItem item, WorkspaceSnapshot snap, bool isNow = false, IReadOnlyDictionary<Guid, TodoItem>? byId = null)
    {
        var metrics = snap.MetricsOf(item);
        var live = item.Status is ItemStatus.Open or ItemStatus.Waiting;
        var pressure = live ? snap.Thresholds.StateOf(metrics.Carry) : PressureState.Fresh;
        var stuck = live && !item.IsSomeday && snap.Thresholds.IsStuck(metrics.Carry, metrics.Defers);
        var usesFallback = item.EstimateMinutes is null && snap.Workspace.CapacityUnit == CapacityUnit.Minutes && live;

        string? breadcrumb = item.ParentId is { } pid && byId?.GetValueOrDefault(pid) is { } parent ? parent.Title : null;

        return new ItemRowViewModel
        {
            Item = item,
            Metrics = metrics,
            Glyph = GlyphFor(item, isNow),
            IsNow = isNow && item.Status == ItemStatus.Open,
            CarryText = live && metrics.Carry > 0 ? metrics.Carry.ToString() : "",
            Pressure = pressure,
            ShowBar = snap.Workspace.Pressure != Pressure.Gentle && pressure is PressureState.Hot or PressureState.Stale,
            IsStuck = stuck,
            EstimateText = item.EstimateMinutes is { } e ? $"~{Duration.Short(e)}" : usesFallback ? $"~{Duration.Short(snap.FallbackMinutes)}*" : "",
            PriorityText = item.Priority > 0 ? $"!{item.Priority}" : "",
            AutomationName = ItemLabels.Describe(item, metrics, snap.Today, isNow, stuck, usesFallback ? snap.FallbackMinutes : null),
            Breadcrumb = breadcrumb,
        };
    }

    // planned / now / waiting / done / dropped — resolved to a vector icon in the view.
    static string GlyphFor(TodoItem item, bool isNow) => item.Status switch
    {
        ItemStatus.Done => "done",
        ItemStatus.Dropped => "dropped",
        ItemStatus.Waiting => "waiting",
        _ => isNow ? "now" : "planned",
    };
}
