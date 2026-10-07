using Noto.App.Logic;
using Noto.Core.Insights;
using Noto.Core.Models;

namespace Noto.App.ViewModels;

public sealed class CapacityViewModel(CapacitySummary summary, bool usesEstimates)
{
    public CapacitySummary Summary { get; } = summary;
    public bool IsOver => Summary.IsOver;
    public double Fraction => Summary.Capacity == 0 ? 0 : Math.Min(1, (double)Summary.Committed / Summary.Capacity);
    public double Percent => Fraction * 100;

    public string UsedText => Format(Summary.Committed);
    public string TotalText => Format(Summary.Capacity);
    public string Text => $"{UsedText} / {TotalText}";
    public string StatusText => IsOver ? $"⚠ {Format(Summary.Over)} over" : "✓ fits";
    public string? Footnote => usesEstimates ? "* items without an estimate count as your median" : null;

    public IReadOnlyList<TodoItem> SuggestedDefers => Summary.SuggestedDefers;
    public bool HasSuggestion => IsOver && SuggestedDefers.Count > 0;
    public string SuggestText => $"suggest deferring {SuggestedDefers.Count}";

    public string AutomationName => $"Capacity {UsedText} of {TotalText}, {(IsOver ? $"{Format(Summary.Over)} over" : "fits")}";

    string Format(int amount) => Summary.Unit == CapacityUnit.Items
        ? amount == 1 ? "1 item" : $"{amount} items"
        : Duration.Short(amount);
}
