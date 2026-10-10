using Noto.App.Services;
using Noto.Core.Layouts;

namespace Noto.App.ViewModels;

// Deadline layout: overdue pinned on top, then dated lanes, "Later", and a No date lane. Nothing needs a date.
public sealed class TimelineViewModel(ListServices services, Guid workspaceId)
    : ItemListViewModel(services, workspaceId)
{
    // The day axis the bars are drawn against. Must match the column count in ListPageView.axaml.
    public const int AxisDays = 14;

    List<SectionViewModel> _sections = [];

    public override IReadOnlyList<SectionViewModel> Sections => _sections;

    public IReadOnlyList<string> AxisLabels { get; private set; } = [];
    public string AxisHint => $"Bars run from today to the due date · {AxisDays} days";

    public override async Task ReloadAsync()
    {
        var keep = FocusedRow?.Id;
        var snap = await Services.Reader.LoadAsync(WorkspaceId);
        Snapshot = snap;
        var byId = snap.Items.ToDictionary(i => i.Id);
        var view = TimelineLayout.Build(snap.Items, snap.Today);

        AxisLabels =
        [
            .. Enumerable
                .Range(0, AxisDays)
                .Select(n =>
                    snap
                        .Today.AddDays(n)
                        .ToString("ddd", System.Globalization.CultureInfo.InvariantCulture)[..1]
                ),
        ];

        // A bar from today to the due date; overdue is pinned to the left edge and marked warm.
        void Place(ItemRowViewModel row, Noto.Core.Models.TodoItem item)
        {
            if (item.DueDate is not { } due)
                return;
            var offset = due.DayNumber - snap.Today.DayNumber;
            row.HasTimelineBar = true;
            row.TimelineOverdue = offset < 0;
            row.TimelineStart = 0;
            row.TimelineSpan = offset < 0 ? 1 : Math.Clamp(offset + 1, 1, AxisDays);
            row.ExtraText =
                offset < 0 ? $"{-offset}d late"
                : offset == 0 ? "due today"
                : $"in {offset}d";
        }

        ItemRowViewModel Row(Noto.Core.Models.TodoItem i)
        {
            var row = Wire(ItemRowFactory.Create(i, snap, snap.Workspace.NowItemId == i.Id, byId));
            row.IsTimeline = true;
            Place(row, i);
            return row;
        }

        SectionViewModel Lane(string title, IEnumerable<Noto.Core.Models.TodoItem> items)
        {
            var s = new SectionViewModel(title);
            s.Replace(items.Select(Row));
            return s;
        }

        var sections = new List<SectionViewModel> { Lane("Overdue", view.Overdue) };
        foreach (var day in view.Days.Where(d => d.Items.Count > 0 || d.Day == snap.Today))
        {
            var label =
                day.Day == snap.Today
                    ? "Today"
                    : day.Day.ToString(
                        "ddd MMM d",
                        System.Globalization.CultureInfo.InvariantCulture
                    );
            sections.Add(Lane(label, day.Items));
        }
        sections.Add(Lane("Later", view.Later));
        sections.Add(Lane("No date", view.NoDate));
        _sections = sections;
        NotifySectionsChanged();
        OnPropertyChanged(nameof(AxisLabels));
        OnPropertyChanged(nameof(AxisHint));
        RestoreFocus(keep);
    }
}
