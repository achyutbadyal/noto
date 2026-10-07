using Noto.App.Services;
using Noto.Core.Layouts;

namespace Noto.App.ViewModels;

// Deadline layout: overdue pinned on top, then dated lanes, "Later", and a No date lane. Nothing needs a date.
public sealed class TimelineViewModel(AppServices services, Guid workspaceId) : ItemListViewModel(services, workspaceId)
{
    List<SectionViewModel> _sections = [];

    public override IReadOnlyList<SectionViewModel> Sections => _sections;

    public override async Task ReloadAsync()
    {
        var keep = FocusedRow?.Id;
        var snap = await Services.Reader.LoadAsync(WorkspaceId);
        Snapshot = snap;
        var byId = snap.Items.ToDictionary(i => i.Id);
        var view = TimelineLayout.Build(snap.Items, snap.Today);

        ItemRowViewModel Row(Noto.Core.Models.TodoItem i) => Wire(ItemRowFactory.Create(i, snap, snap.Workspace.NowItemId == i.Id, byId));
        SectionViewModel Lane(string title, IEnumerable<Noto.Core.Models.TodoItem> items)
        {
            var s = new SectionViewModel(title);
            s.Replace(items.Select(Row));
            return s;
        }

        var sections = new List<SectionViewModel> { Lane("Overdue", view.Overdue) };
        foreach (var day in view.Days.Where(d => d.Items.Count > 0 || d.Day == snap.Today))
        {
            var label = day.Day == snap.Today ? "Today" : day.Day.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture);
            sections.Add(Lane(label, day.Items));
        }
        sections.Add(Lane("Later", view.Later));
        sections.Add(Lane("No date", view.NoDate));
        _sections = sections;
        OnPropertyChanged(nameof(Sections));
        RestoreFocus(keep);
    }
}
