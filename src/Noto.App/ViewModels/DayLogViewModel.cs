using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Noto.App.Services;
using Noto.Core.Derivations;
using Noto.Core.Models;
using Noto.Core.Text;

namespace Noto.App.ViewModels;

public sealed record DayLogEntry(string Title, string Detail);

public sealed class DayLogSection(string title, IReadOnlyList<DayLogEntry> entries)
{
    public string Title { get; } = title;
    public IReadOnlyList<DayLogEntry> Entries { get; } = entries;
    public string Header => $"{Title.ToUpperInvariant()} ({Entries.Count})";
}

// Time travel (docs/07 §8.3): past days are a read-only log rebuilt from history; future days list what's scheduled.
public sealed partial class DayLogViewModel(WorkspaceReader reader, Guid workspaceId)
    : ObservableObject
{
    public ObservableCollection<DayLogSection> Sections { get; } = [];

    [ObservableProperty]
    DateOnly _day;

    [ObservableProperty]
    bool _isFuture;

    [ObservableProperty]
    string _title = "";

    [ObservableProperty]
    string _summary = "";

    public async Task LoadAsync(DateOnly day)
    {
        var snap = await reader.LoadAsync(workspaceId);
        Day = day;
        IsFuture = day > snap.Today;
        Title =
            day.ToString("dddd, MMM d", System.Globalization.CultureInfo.InvariantCulture)
            + (IsFuture ? " · scheduled" : " · read-only log");
        Sections.Clear();
        var byId = snap.Items.ToDictionary(i => i.Id);

        if (IsFuture)
        {
            var scheduled = snap
                .Items.Where(i =>
                    i.Status == ItemStatus.Open && !i.IsContainer && i.PlannedFor == day
                )
                .ToList();
            Sections.Add(Section("Planned", scheduled.Select(i => i.Id), byId));
            Summary =
                scheduled.Count == 0
                    ? "Nothing planned for this day yet."
                    : $"{scheduled.Count} planned";
            return;
        }

        var sets = await reader.DaySetsAsync(workspaceId, day);
        Sections.Add(Section("Carried in", sets.CarriedIn, byId));
        Sections.Add(Section("Planned", sets.PlannedIn.Concat(sets.Added), byId));
        Sections.Add(Section("Done", sets.Done, byId));
        Sections.Add(Section("Dropped", sets.Dropped, byId));
        Sections.Add(Section("Deferred", sets.DeferredOut, byId));
        Sections.Add(Section("Left over", sets.OpenAtEnd, byId));
        var committed = sets.CarriedIn.Count + sets.PlannedIn.Count + sets.Added.Count;
        Summary =
            committed == 0
                ? "Nothing was planned."
                : $"{sets.Done.Count} of {committed} done · {sets.OpenAtEnd.Count} left over";
    }

    static DayLogSection Section(
        string title,
        IEnumerable<Guid> ids,
        Dictionary<Guid, TodoItem> byId
    ) =>
        new(
            title,
            ids.Distinct()
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .Select(i => new DayLogEntry(
                    i.Title,
                    i.EstimateMinutes is { } m ? $"~{Noto.Core.Text.Duration.Short(m)}" : ""
                ))
                .ToList()
        );
}
