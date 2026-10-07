using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Noto.Core.Derivations;

namespace Noto.App.ViewModels;

public sealed record DayBarViewModel(DateOnly Day, double Height, string Label, string Tooltip, bool IsToday, bool IsSelected)
{
    public string AutomationName => Tooltip;
}

// Completion sparkline for the last 14 days. Clicking a bar jumps to that day (time travel).
public sealed partial class DayStripViewModel : ObservableObject
{
    public const int Days = 14;
    public const double MaxHeight = 28;

    public ObservableCollection<DayBarViewModel> Bars { get; } = [];

    public void Load(DateOnly today, IReadOnlyList<DayStats> history, DateOnly? selected)
    {
        var byDay = history.ToDictionary(d => d.Day);
        Bars.Clear();
        for (var offset = Days - 1; offset >= 0; offset--)
        {
            var day = today.AddDays(-offset);
            var label = day.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture);
            if (!byDay.TryGetValue(day, out var stats))
            {
                var empty = day == today ? $"{label}: today" : $"{label}: nothing planned";
                Bars.Add(new(day, 3, day.ToString("ddd")[..1], empty, day == today, day == (selected ?? today)));
                continue;
            }

            var rate = stats.CompletionRate ?? 0;
            var tooltip = stats.CompletionRate is null ? $"{label}: nothing planned" : $"{label}: done {stats.Done} of {stats.Done + stats.OpenAtEnd}";
            Bars.Add(new(day, Math.Max(3, rate * MaxHeight), day.ToString("ddd")[..1], tooltip, day == today, day == (selected ?? today)));
        }
    }
}
