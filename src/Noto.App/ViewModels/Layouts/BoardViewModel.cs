using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Commands;
using Noto.Core.Layouts;
using Noto.Core.Models;

namespace Noto.App.ViewModels;

// Board layout: fixed columns mapped to item state, user columns in between, WIP limits (docs/03, docs/07 §9.1).
// Status stays the single source of truth; moving between columns is just ordinary commands.
public sealed partial class BoardViewModel : ItemListViewModel
{
    List<SectionViewModel> _columns = [];
    List<BoardColumn> _model = [];
    string? _wipOverrideFor;

    public BoardViewModel(AppServices services, Guid workspaceId)
        : base(services, workspaceId) { }

    public override IReadOnlyList<SectionViewModel> Sections => _columns;
    public IReadOnlyList<SectionViewModel> Columns => _columns;

    [ObservableProperty]
    string _newColumnName = "";

    [ObservableProperty]
    string _newColumnWip = "";

    public override async Task ReloadAsync()
    {
        var keep = FocusedRow?.Id;
        var snap = await Services.Reader.LoadAsync(WorkspaceId);
        Snapshot = snap;
        var settings = BoardSettings.FromJson(snap.Workspace.LayoutSettingsJson);
        _model = BoardLayout.Build(snap.Items, snap.Today, settings).ToList();
        var byId = snap.Items.ToDictionary(i => i.Id);

        // Items parked in a column for N days: the only pressure signal under gentle pressure.
        var records = await Services.Reader.RecordsAsync(WorkspaceId);
        var stuck = BoardLayout
            .StuckInColumn(
                records.Select(r => (r.Item, r.Events)),
                snap.Today,
                snap.Workspace.DayBoundary,
                settings.StuckDays
            )
            .ToHashSet();

        var collapsed = _columns.ToDictionary(c => c.Title, c => c.IsCollapsed);
        _columns = _model
            .Select(col =>
            {
                var section = new SectionViewModel(col.Name) { WipLimit = col.WipLimit };
                section.Replace(
                    col.Items.Select(i =>
                    {
                        var row = Wire(
                            ItemRowFactory.Create(i, snap, snap.Workspace.NowItemId == i.Id, byId)
                        );
                        if (stuck.Contains(i.Id))
                            row.ExtraText =
                                $"⏸ {(snap.Today.DayNumber - LastMoveDay(records, i, snap)).ToString()}d here";
                        return row;
                    })
                );
                return section;
            })
            .ToList();
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Sections));
        RestoreFocus(keep);
    }

    static int LastMoveDay(
        IReadOnlyList<Noto.Core.Insights.ItemRecord> records,
        TodoItem item,
        WorkspaceSnapshot snap
    )
    {
        var events = records.First(r => r.Item.Id == item.Id).Events;
        return events
            .Where(e =>
                e.Type
                    is ItemEventType.ColumnChanged
                        or ItemEventType.Planned
                        or ItemEventType.Created
            )
            .Select(e =>
                Noto.Core.Time.LogicalDate.Of(
                    e.OccurredAt,
                    e.Tz,
                    snap.Workspace.DayBoundary
                ).DayNumber
            )
            .DefaultIfEmpty(snap.Today.DayNumber)
            .Max();
    }

    // h/l and ←/→ move focus between columns; ⇧← / ⇧→ move the item itself.
    public override async Task<bool> HandleKeyAsync(KeyChord chord)
    {
        if (!IsEditingTitle && Decisions.Prompt is null)
        {
            switch (chord)
            {
                case { Key: "ArrowRight" or "l", Shift: false, Command: false }:
                    FocusColumn(1);
                    return true;
                case { Key: "ArrowLeft" or "h", Shift: false, Command: false }:
                    FocusColumn(-1);
                    return true;
                case { Key: "ArrowRight" or "l", Shift: true }:
                    await MoveItemAsync(1);
                    return true;
                case { Key: "ArrowLeft" or "h", Shift: true }:
                    await MoveItemAsync(-1);
                    return true;
                case { Key: "j" or "ArrowDown", Shift: false }:
                    StepInColumn(1);
                    return true;
                case { Key: "k" or "ArrowUp", Shift: false }:
                    StepInColumn(-1);
                    return true;
            }
        }
        return await base.HandleKeyAsync(chord);
    }

    int ColumnOf(ItemRowViewModel? row) =>
        row is null ? -1 : _columns.FindIndex(c => c.Rows.Contains(row));

    void FocusColumn(int delta)
    {
        if (FocusedRow is null)
        {
            SetFocus(_columns.SelectMany(c => c.Rows).FirstOrDefault());
            return;
        }
        var from = ColumnOf(FocusedRow);
        var rowIndex = _columns[from].Rows.IndexOf(FocusedRow);

        // Skip empty columns so h/l always lands on an item.
        for (var c = from + delta; c >= 0 && c < _columns.Count; c += delta)
        {
            if (_columns[c].Rows.Count == 0)
                continue;
            SetFocus(_columns[c].Rows[Math.Min(rowIndex, _columns[c].Rows.Count - 1)]);
            return;
        }
    }

    void StepInColumn(int delta)
    {
        if (FocusedRow is null)
        {
            FocusColumn(1);
            return;
        }
        var column = _columns[ColumnOf(FocusedRow)];
        var i = Math.Clamp(column.Rows.IndexOf(FocusedRow) + delta, 0, column.Rows.Count - 1);
        SetFocus(column.Rows[i]);
    }

    public async Task MoveItemAsync(int columnDelta)
    {
        if (FocusedRow is not { } row || Snapshot is not { } snap)
            return;
        var from = ColumnOf(row);
        var target = from + columnDelta;
        if (target < 0 || target >= _columns.Count)
            return;
        await MoveToColumnAsync(row.Item, _model[target].Id);
    }

    public async Task MoveToColumnAsync(TodoItem item, string columnId, string? waitingOn = null)
    {
        var snap = Snapshot!;
        var move = BoardMoves.Plan(item, columnId, snap.Today, waitingOn);
        if (move.NeedsWaitingOn)
        {
            await Decisions.BeginAsync(DecisionKind.WaitOn, [item], Context);
            return;
        }
        if (move.Commands.Count == 0)
            return;

        // A column at its WIP limit asks first; repeating the move overrides.
        var column = _model.First(c => c.Id == columnId);
        if (
            column.WipLimit is { } limit
            && column.Items.Count >= limit
            && _wipOverrideFor != $"{item.Id}:{columnId}"
        )
        {
            _wipOverrideFor = $"{item.Id}:{columnId}";
            Message =
                $"“{column.Name}” is full ({column.Items.Count}/{limit}). Move one out, or repeat to override.";
            Decisions.Message = Message;
            return;
        }
        _wipOverrideFor = null;
        await Services.Runner.RunAllAsync(move.Commands, $"Moved “{item.Title}” to {column.Name}");
    }

    [RelayCommand]
    async Task AddColumnAsync()
    {
        var name = NewColumnName.Trim();
        if (name.Length == 0)
            return;
        int? wip = int.TryParse(NewColumnWip, out var n) && n > 0 ? n : null;

        var snap = Snapshot ?? await Services.Reader.LoadAsync(WorkspaceId);
        var settings = BoardSettings.FromJson(snap.Workspace.LayoutSettingsJson);
        var columns = settings
            .UserColumns.Append(
                new BoardColumnDef(Guid.CreateVersion7().ToString("N")[..8], name, wip)
            )
            .ToList();
        await Services.Workspaces.UpdateAsync(
            WorkspaceId,
            ws =>
                ws.LayoutSettingsJson = (settings with { UserColumns = columns }).ToJson(
                    ws.LayoutSettingsJson
                )
        );
        NewColumnName = NewColumnWip = "";
        Services.Runner.NotifyChanged();
    }
}
