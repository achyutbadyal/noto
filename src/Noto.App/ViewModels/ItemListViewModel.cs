using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Commands;
using Noto.Core.Models;

namespace Noto.App.ViewModels;

// Shared behavior of every flat item screen: focus, multi-select, and the single-key decisions.
public abstract partial class ItemListViewModel : ObservableObject
{
    protected ItemListViewModel(AppServices services, Guid workspaceId)
    {
        Services = services;
        WorkspaceId = workspaceId;
        Decisions = new DecisionController(services);
    }

    protected AppServices Services { get; }
    public Guid WorkspaceId { get; }
    public DecisionController Decisions { get; }
    public WorkspaceSnapshot? Snapshot { get; protected set; }
    public abstract IReadOnlyList<SectionViewModel> Sections { get; }

    // The snapshot of the focused row's workspace (differs per row on Today (all)).
    public virtual WorkspaceSnapshot? FocusedSnapshot => Snapshot;

    [ObservableProperty]
    ItemRowViewModel? _focusedRow;

    [ObservableProperty]
    bool _isEditingTitle;

    [ObservableProperty]
    string _editText = "";

    [ObservableProperty]
    string? _message;

    public event Action? NewItemRequested;
    public event Action<ItemRowViewModel?>? FocusChanged;

    public abstract Task ReloadAsync();

    public IReadOnlyList<ItemRowViewModel> FlatRows =>
        Sections.Where(s => !s.IsCollapsed).SelectMany(s => s.Rows).ToList();

    public IReadOnlyList<ItemRowViewModel> SelectedRows =>
        FlatRows.Where(r => r.IsSelected).ToList();

    // Decisions apply to the multi-selection, or to the focused row when nothing is selected.
    public IReadOnlyList<TodoItem> Targets()
    {
        var selected = SelectedRows;
        var rows =
            selected.Count > 0 ? selected
            : FocusedRow is { } f ? [f]
            : [];
        return rows.Select(r => r.Item).ToList();
    }

    protected void RestoreFocus(Guid? keepId)
    {
        var flat = FlatRows;
        var row = flat.FirstOrDefault(r => r.Id == keepId) ?? flat.FirstOrDefault();
        SetFocus(row);
    }

    public void SetFocus(ItemRowViewModel? row)
    {
        if (FocusedRow is { } old)
            old.IsFocused = false;
        FocusedRow = row;
        if (row is not null)
            row.IsFocused = true;
        FocusChanged?.Invoke(row);
    }

    public void Move(int delta, bool extendSelection = false)
    {
        var flat = FlatRows;
        if (flat.Count == 0)
            return;
        var from = FocusedRow is null ? -1 : flat.ToList().IndexOf(FocusedRow);
        var to = Math.Clamp(from + delta, 0, flat.Count - 1);
        if (extendSelection)
        {
            if (FocusedRow is { } current)
                current.IsSelected = true;
            flat[to].IsSelected = true;
        }
        else
            foreach (var r in flat)
                r.IsSelected = false;
        SetFocus(flat[to]);
    }

    public void SelectAll()
    {
        foreach (var r in FlatRows)
            r.IsSelected = true;
    }

    public void ClearSelection()
    {
        foreach (var r in FlatRows)
            r.IsSelected = false;
    }

    protected DateOnly TodayOrFallback =>
        Snapshot?.Today ?? DateOnly.FromDateTime(Services.Clock.UtcNow.UtcDateTime);

    protected virtual DecisionContext Context => new(TodayOrFallback);

    // Returns true when the key was consumed. Page-level keys (day nav, help, go-to) are left to the shell.
    public virtual async Task<bool> HandleKeyAsync(KeyChord chord)
    {
        if (IsEditingTitle)
            return await HandleEditKeyAsync(chord);
        if (Decisions.Prompt is not null)
            return await Decisions.HandlePromptKeyAsync(chord);

        var binding = KeyMap.Resolve(KeyScope.List, chord);
        if (binding is null)
            return false;

        switch (binding.Action)
        {
            case AppAction.MoveDown:
                Move(1);
                return true;
            case AppAction.MoveUp:
                Move(-1);
                return true;
            case AppAction.ExtendDown:
                Move(1, extendSelection: true);
                return true;
            case AppAction.ExtendUp:
                Move(-1, extendSelection: true);
                return true;
            case AppAction.SelectAll:
                SelectAll();
                return true;
            case AppAction.NewItem:
                NewItemRequested?.Invoke();
                return true;
            case AppAction.Edit:
                BeginEdit();
                return true;
            case AppAction.MakeNow:
                await MakeNowAsync();
                return true;
            case AppAction.SetPriority:
                await SetPriorityAsync(binding.Arg);
                return true;
            case AppAction.Complete:
                await Decisions.BeginAsync(DecisionKind.Complete, Targets(), Context);
                return true;
            case AppAction.Defer:
                await Decisions.BeginAsync(DecisionKind.Defer, Targets(), Context);
                return true;
            case AppAction.KeepToday:
                await Decisions.BeginAsync(DecisionKind.KeepToday, Targets(), Context);
                return true;
            case AppAction.Someday:
                await Decisions.BeginAsync(DecisionKind.Someday, Targets(), Context);
                return true;
            case AppAction.WaitOn:
                await Decisions.BeginAsync(DecisionKind.WaitOn, Targets(), Context);
                return true;
            case AppAction.BreakDown:
                await Decisions.BeginAsync(DecisionKind.BreakDown, Targets(), Context);
                return true;
            case AppAction.Drop:
                await Decisions.BeginAsync(DecisionKind.Drop, Targets(), Context);
                return true;
            case AppAction.SetEstimate:
                await Decisions.BeginAsync(DecisionKind.SetEstimate, Targets(), Context);
                return true;
            default:
                return false;
        }
    }

    [RelayCommand]
    async Task PressAsync(string key) => await HandleKeyAsync(new KeyChord(key));

    [RelayCommand]
    void FocusRow(ItemRowViewModel row) => SetFocus(row);

    [RelayCommand]
    async Task ToggleCompleteAsync(ItemRowViewModel row)
    {
        SetFocus(row);
        await HandleKeyAsync(new KeyChord("x"));
    }

    public void BeginEdit()
    {
        if (FocusedRow is not { } row)
            return;
        EditText = row.Title;
        row.IsEditing = true;
        IsEditingTitle = true;
    }

    partial void OnIsEditingTitleChanged(bool value)
    {
        if (!value)
            foreach (var r in FlatRows)
                r.IsEditing = false;
    }

    protected ItemRowViewModel Wire(ItemRowViewModel row)
    {
        row.Owner = this;
        return row;
    }

    async Task<bool> HandleEditKeyAsync(KeyChord chord)
    {
        if (chord.Key == "Escape")
        {
            IsEditingTitle = false;
            return true;
        }
        if (chord.Key == "Enter")
        {
            await CommitEditAsync();
            return true;
        }
        return false;
    }

    [RelayCommand]
    public async Task CommitEditAsync()
    {
        if (!IsEditingTitle || FocusedRow is not { } row)
            return;
        IsEditingTitle = false;
        if (EditText.Trim() == row.Title || EditText.Trim().Length == 0)
            return;
        await Services.Runner.RunAsync(new RenameItem(row.Id, EditText), "Renamed");
    }

    async Task SetPriorityAsync(int priority)
    {
        var targets = Targets();
        if (targets.Count == 0)
            return;
        await Services.Runner.RunAllAsync(
            targets.Select(t => (ItemCommand)new SetPriority(t.Id, priority)).ToList(),
            $"Priority {priority}"
        );
    }

    async Task MakeNowAsync()
    {
        if (FocusedRow is not { } row || row.Item.Status != ItemStatus.Open)
            return;
        await Services.Focus.ToggleAsync(row.Item.WorkspaceId, row.Item);
    }
}
