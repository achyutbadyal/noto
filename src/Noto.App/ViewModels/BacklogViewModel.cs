using Noto.App.Services;
using Noto.Core.Models;

namespace Noto.App.ViewModels;

// Unscheduled and Someday items. Neither builds carry, so a big backlog never turns Today red.
public sealed class BacklogViewModel : ItemListViewModel
{
    readonly SectionViewModel _unscheduled = new("Unscheduled");
    readonly SectionViewModel _someday = new("Someday");

    public BacklogViewModel(
        AppServices services,
        Guid workspaceId,
        Func<string, Guid?>? resolveWorkspace = null
    )
        : base(services, workspaceId)
    {
        Sections = [_unscheduled, _someday];
        Add = new AddItemViewModel(
            services,
            workspaceId,
            plannedForToday: false,
            resolveWorkspace,
            () => TodayOrFallback
        );
    }

    public override IReadOnlyList<SectionViewModel> Sections { get; }
    public AddItemViewModel Add { get; }

    public override async Task ReloadAsync()
    {
        var keep = FocusedRow?.Id;
        var snap = await Services.Reader.LoadAsync(WorkspaceId);
        Snapshot = snap;
        var byId = snap.Items.ToDictionary(i => i.Id);

        var open = snap.Items.Where(i => i.Status == ItemStatus.Open && !i.IsContainer);
        _unscheduled.Replace(
            open.Where(i => !i.IsSomeday && i.PlannedFor is null)
                .OrderByDescending(i => i.Priority)
                .ThenBy(i => i.CreatedAt)
                .Select(i => Wire(ItemRowFactory.Create(i, snap, false, byId)))
        );
        _someday.Replace(
            open.Where(i => i.IsSomeday)
                .OrderBy(i => i.CreatedAt)
                .Select(i => Wire(ItemRowFactory.Create(i, snap, false, byId)))
        );
        RestoreFocus(keep);
    }
}
